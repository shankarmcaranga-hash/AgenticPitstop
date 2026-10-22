using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace AgenticPitstop.Infrastructure;

/// <summary>
/// The deterministic API executor.
///
/// No LLM and no business decision. It reloads the approved package from SQL,
/// re-verifies every control, sends exactly what the approver saw, and
/// classifies the response. Any control failure blocks the send before a socket
/// is opened - fail closed, always.
/// </summary>
public sealed class ApiConfigExecutionEngine
{
    public const string HttpClientName = "ApiConfiguratorTarget";

    private readonly ApiConfigDataAccess _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ApiConfigSecretResolver _secrets;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApiConfigExecutionEngine> _logger;

    public ApiConfigExecutionEngine(ApiConfigDataAccess db, IHttpClientFactory httpClientFactory,
                                    ApiConfigSecretResolver secrets, IConfiguration configuration,
                                    ILogger<ApiConfigExecutionEngine> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _secrets = secrets;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Processes one approved execution. Safe to call twice for the same id -
    /// the SQL claim makes the second call a no-op, which is what at-least-once
    /// queue delivery requires.
    /// </summary>
    public async Task<string> ProcessAsync(int executionId, CancellationToken cancellationToken)
    {
        var claimed = ApiConfigDataAccess.Out("@Claimed", SqlDbType.Bit);
        _db.Execute("dbo.API_Config_Execution_Claim", ApiConfigDataAccess.P("@ExecutionId", executionId), claimed);

        if (claimed.Value == DBNull.Value || !Convert.ToBoolean(claimed.Value))
        {
            _logger.LogInformation("Execution {ExecutionId} was already claimed. Skipping duplicate.", executionId);
            return "AlreadyClaimed";
        }

        var data = _db.DataSet("dbo.API_Config_Execution_GetById", ApiConfigDataAccess.P("@ExecutionId", executionId));
        var request = ApiConfigDataAccess.First(data.Tables[0]);

        if (request == null)
        {
            Complete(executionId, "TechnicalFailed", "The execution record could not be reloaded.");
            return "NotFound";
        }

        var headers = data.Tables.Count > 2 ? data.Tables[2] : new DataTable();
        var successCodes = data.Tables.Count > 3 ? data.Tables[3] : new DataTable();

        var blocked = RunPreSendChecks(request);
        if (blocked != null)
        {
            RecordAttempt(executionId, null, "TechnicalFailed", 0, null, blocked);
            Complete(executionId, "ApprovalInvalidated", blocked);
            _logger.LogWarning("Execution {ExecutionId} blocked before send: {Reason}", executionId, blocked);
            return "Blocked";
        }

        var maxAttempts = ApiConfigDataAccess.Int(request, "MaxAttempts", 1);
        var retrySafe = ApiConfigDataAccess.Bool(request, "IsRetrySafe");
        var method = ApiConfigDataAccess.Str(request, "ResolvedMethod");

        // Never retry an unsafe method the target has not guaranteed as idempotent.
        var unsafeMethod = method is "POST" or "PATCH";
        var attemptBudget = (unsafeMethod && !retrySafe) ? 1 : Math.Max(1, maxAttempts);

        for (var attempt = 1; attempt <= attemptBudget; attempt++)
        {
            var outcome = await SendOnceAsync(executionId, request, headers, successCodes, cancellationToken);

            if (outcome.Outcome is "Succeeded" or "BusinessFailed")
            {
                Complete(executionId, outcome.Outcome, outcome.Error);
                return outcome.Outcome;
            }

            if (outcome.Outcome == "UnknownOutcome")
            {
                // A timeout after the request was written is never retried blindly:
                // the target may already have applied it.
                Complete(executionId, "UnknownOutcome",
                    "The outcome could not be determined. A human must confirm the target state before any resubmission.");
                return "UnknownOutcome";
            }

            if (attempt < attemptBudget)
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
        }

        Complete(executionId, "TechnicalFailed", "All permitted attempts failed with a technical error.");
        return "TechnicalFailed";
    }

    // -------------------------------------------------------------------------
    // Controls. Each returns a reason when it blocks, or null when it passes.
    // -------------------------------------------------------------------------
    private string RunPreSendChecks(DataRow request)
    {
        if (ApiConfigDataAccess.Str(request, "ApprovalStatus") != "Approved")
            return "The execution is not approved.";

        var storedHash = ApiConfigDataAccess.Str(request, "ApprovalHash");
        var recomputed = ComputeApprovalHash(
            ApiConfigDataAccess.Str(request, "ExecutionId"),
            ApiConfigDataAccess.Str(request, "ResolvedMethod"),
            ApiConfigDataAccess.Str(request, "ResolvedUrl"),
            ApiConfigDataAccess.Str(request, "ResolvedPayloadJson"),
            ApiConfigDataAccess.Str(request, "ConfigHash"));

        if (string.IsNullOrEmpty(storedHash) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(storedHash), Encoding.ASCII.GetBytes(recomputed)))
            return "Approval hash mismatch. The package changed after approval.";

        var url = ApiConfigDataAccess.Str(request, "ResolvedUrl");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var target))
            return "The resolved URL is not a valid absolute URL.";

        if (target.Scheme != Uri.UriSchemeHttps && !_configuration.GetValue("ApiConfigurator:AllowNonHttpsTargets", false))
            return "The resolved URL is not HTTPS.";

        var versionId = ApiConfigDataAccess.Int(request, "ConnectorVersionId");
        var allowedHosts = _db.DataSet("dbo.API_Config_ConnectorVersion_Get",
                                       ApiConfigDataAccess.P("@ConnectorVersionId", versionId)).Tables[3];

        var allowed = allowedHosts.Rows.Cast<DataRow>()
            .Any(r => string.Equals(ApiConfigDataAccess.Str(r, "HostName"), target.Host, StringComparison.OrdinalIgnoreCase));

        if (!allowed)
            return $"Host '{target.Host}' is not in the approved allowlist for this connector version.";

        return null;
    }

    /// <summary>
    /// Recomputes the approval hash exactly as API_Config_Execution_Approve does.
    /// SQL hashes NVARCHAR, so the bytes must be UTF-16LE and the canonical
    /// string must be assembled in the same order on both sides.
    /// </summary>
    public static string ComputeApprovalHash(string executionId, string method, string url, string payload, string configHash)
    {
        var canonical = string.Join('|',
            (executionId ?? string.Empty).ToUpperInvariant(),
            method ?? string.Empty,
            url ?? string.Empty,
            payload ?? string.Empty,
            configHash ?? string.Empty);

        return Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(canonical)));
    }

    // -------------------------------------------------------------------------
    private async Task<(string Outcome, int? StatusCode, string Error)> SendOnceAsync(
        int executionId, DataRow request, DataTable headers, DataTable successCodes, CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        var method = ApiConfigDataAccess.Str(request, "ResolvedMethod");
        var url = ApiConfigDataAccess.Str(request, "ResolvedUrl");
        var payload = ApiConfigDataAccess.Str(request, "ResolvedPayloadJson");
        var timeout = ApiConfigDataAccess.Int(request, "TimeoutSeconds", 30);

        // ExecutionUid, not the INT key, is what the target system sees. An
        // IDENTITY value is only unique inside this database, which is not
        // enough for a key a customer's API will deduplicate on.
        var executionUid = ApiConfigDataAccess.Str(request, "ExecutionUid");

        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(timeout);

        using var message = new HttpRequestMessage(new HttpMethod(method), url);

        if (!string.IsNullOrWhiteSpace(payload) && method is not ("GET" or "DELETE"))
            message.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        foreach (DataRow header in headers.Rows)
        {
            var name = ApiConfigDataAccess.Str(header, "HeaderName");
            if (string.IsNullOrWhiteSpace(name) ||
                name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;

            var value = ApiConfigPayloadResolver.Resolve(
                ApiConfigDataAccess.Str(header, "HeaderValueTemplate"),
                ApiConfigDataAccess.Str(request, "SourceDataJson"),
                executionUid,
                out _);

            message.Headers.TryAddWithoutValidation(name, value);
        }

        var idempotencyHeader = ApiConfigDataAccess.Str(request, "IdempotencyHeader");
        if (!string.IsNullOrWhiteSpace(idempotencyHeader))
            message.Headers.TryAddWithoutValidation(idempotencyHeader, executionUid);

        var correlationHeader = ApiConfigDataAccess.Str(request, "CorrelationHeader");
        if (!string.IsNullOrWhiteSpace(correlationHeader))
            message.Headers.TryAddWithoutValidation(correlationHeader, executionUid);

        await ApplyAuthenticationAsync(message, request, cancellationToken);

        try
        {
            using var response = await client.SendAsync(message, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var duration = (int)(DateTime.UtcNow - started).TotalMilliseconds;

            var isSuccess = successCodes.Rows.Cast<DataRow>()
                .Any(r => ApiConfigDataAccess.Int(r, "StatusCode") == (int)response.StatusCode);

            var outcome = isSuccess ? "Succeeded" : ClassifyFailure((int)response.StatusCode);

            RecordAttempt(executionId, (int)response.StatusCode, outcome, duration, Truncate(body, 3900), null);
            return (outcome, (int)response.StatusCode, null);
        }
        catch (TaskCanceledException ex)
        {
            var duration = (int)(DateTime.UtcNow - started).TotalMilliseconds;
            RecordAttempt(executionId, null, "UnknownOutcome", duration, null,
                          "Request timed out. The target may or may not have applied it.");
            _logger.LogWarning(ex, "Execution {ExecutionId} timed out.", executionId);
            return ("UnknownOutcome", null, "Timeout");
        }
        catch (HttpRequestException ex)
        {
            var duration = (int)(DateTime.UtcNow - started).TotalMilliseconds;
            RecordAttempt(executionId, null, "TechnicalFailed", duration, null, Truncate(ex.Message, 1900));
            return ("TechnicalFailed", null, ex.Message);
        }
    }

    /// <summary>
    /// 4xx other than 408/429 is a business or contract failure and is never
    /// retried. 5xx, 408 and 429 are technical and may be retried.
    /// </summary>
    private static string ClassifyFailure(int statusCode)
    {
        if (statusCode is 408 or 429) return "TechnicalFailed";
        if (statusCode >= 500) return "TechnicalFailed";
        return "BusinessFailed";
    }
    /// <summary>
    /// Applies a connector version's published authentication to a request built
    /// elsewhere. Pipelines use this so auth is configured in one place only.
    /// </summary>
    public Task AuthenticateAsync(HttpRequestMessage message, int connectorVersionId, CancellationToken cancellationToken)
    {
        var holder = new DataTable();
        holder.Columns.Add("ConnectorVersionId", typeof(int));
        holder.Rows.Add(connectorVersionId);
        return ApplyAuthenticationAsync(message, holder.Rows[0], cancellationToken);
    }
    private async Task ApplyAuthenticationAsync(HttpRequestMessage message, DataRow request, CancellationToken cancellationToken)
    {
        var versionId = ApiConfigDataAccess.Int(request, "ConnectorVersionId");
        var auth = ApiConfigDataAccess.First(
            _db.DataSet("dbo.API_Config_ConnectorVersion_Get",
                        ApiConfigDataAccess.P("@ConnectorVersionId", versionId)).Tables[5]);

        if (auth == null) return;

        var authType = ApiConfigDataAccess.Str(auth, "AuthType");
        var alias = ApiConfigDataAccess.Str(auth, "SecretStoreAlias");

        switch (authType)
        {
            case "ApiKey":
            {
                var key = await _secrets.ResolveAsync(alias, ApiConfigDataAccess.Str(auth, "ApiKeySecretRef"), cancellationToken);
                if (!string.IsNullOrEmpty(key) &&
                    string.Equals(ApiConfigDataAccess.Str(auth, "ApiKeyLocation"), "Header", StringComparison.OrdinalIgnoreCase))
                    message.Headers.TryAddWithoutValidation(ApiConfigDataAccess.Str(auth, "ApiKeyName"), key);
                break;
            }
            case "BearerToken":
            {
                var token = await _secrets.ResolveAsync(alias, ApiConfigDataAccess.Str(auth, "BearerTokenSecretRef"), cancellationToken);
                if (!string.IsNullOrEmpty(token))
                    message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                break;
            }
            case "Basic":
            {
                var user = await _secrets.ResolveAsync(alias, ApiConfigDataAccess.Str(auth, "UsernameSecretRef"), cancellationToken);
                var password = await _secrets.ResolveAsync(alias, ApiConfigDataAccess.Str(auth, "PasswordSecretRef"), cancellationToken);
                if (!string.IsNullOrEmpty(user))
                {
                    var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));
                    message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", encoded);
                }
                break;
            }
            case "OAuth2ClientCredentials":
            {
                var token = await GetOAuthTokenAsync(auth, cancellationToken);
                if (!string.IsNullOrEmpty(token))
                    message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                break;
            }
            case "CustomPlugin":
            {
                // Extension point for schemes that need more than one credential -
                // for example a client_id and client_secret pair in two headers.
                // Resolve each reference from AdditionalSettingsJson and add the
                // headers here. Nothing is added by default, on purpose.
                break;
            }
        }
    }

    private async Task<string> GetOAuthTokenAsync(DataRow auth, CancellationToken cancellationToken)
    {
        var alias = ApiConfigDataAccess.Str(auth, "SecretStoreAlias");
        var clientId = await _secrets.ResolveAsync(alias, ApiConfigDataAccess.Str(auth, "ClientIdSecretRef"), cancellationToken);
        var clientSecret = await _secrets.ResolveAsync(alias, ApiConfigDataAccess.Str(auth, "ClientSecretSecretRef"), cancellationToken);
        var tokenEndpoint = ApiConfigDataAccess.Str(auth, "TokenEndpoint");

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(tokenEndpoint)) return null;

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret ?? string.Empty
        };

        var scope = ApiConfigDataAccess.Str(auth, "Scope");
        if (!string.IsNullOrWhiteSpace(scope)) form["scope"] = scope;

        var audience = ApiConfigDataAccess.Str(auth, "Audience");
        if (!string.IsNullOrWhiteSpace(audience)) form["audience"] = audience;

        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var response = await client.PostAsync(tokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = System.Text.Json.JsonDocument.Parse(json);

        return document.RootElement.TryGetProperty("access_token", out var element) ? element.GetString() : null;
    }

    private void RecordAttempt(int executionId, int? statusCode, string outcome, int durationMs, string snippet, string error)
    {
        _db.Execute("dbo.API_Config_Execution_RecordAttempt",
            ApiConfigDataAccess.P("@ExecutionId", executionId),
            ApiConfigDataAccess.P("@HttpStatusCode", (object)statusCode ?? DBNull.Value),
            ApiConfigDataAccess.P("@Outcome", outcome),
            ApiConfigDataAccess.P("@DurationMs", durationMs),
            ApiConfigDataAccess.P("@ResponseSnippet", (object)snippet ?? DBNull.Value),
            ApiConfigDataAccess.P("@ErrorMessage", (object)error ?? DBNull.Value));
    }

    private void Complete(int executionId, string status, string reason)
    {
        _db.Execute("dbo.API_Config_Execution_Complete",
            ApiConfigDataAccess.P("@ExecutionId", executionId),
            ApiConfigDataAccess.P("@Status", status),
            ApiConfigDataAccess.P("@Reason", (object)reason ?? DBNull.Value));
    }

    private static string Truncate(string value, int length)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= length ? value : value[..length];
    }
}

/// <summary>
/// Drains the API Configurator outbox.
///
/// With SQS enabled the worker publishes each message and lets a queue consumer
/// execute it. Without SQS it executes in process, so the full human-in-the-loop
/// flow works with nothing but SQL Server.
///
/// Either way the execution id is the idempotency key and
/// API_Config_Execution_Claim guarantees a duplicate message never creates a
/// second business transaction.
/// </summary>
public sealed class ApiConfigExecutionWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApiConfigExecutionWorker> _logger;

    public ApiConfigExecutionWorker(IServiceProvider services, IConfiguration configuration,
                                    ILogger<ApiConfigExecutionWorker> logger)
    {
        _services = services;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollSeconds = Math.Max(1, _configuration.GetValue("ApiConfigurator:OutboxPollSeconds", 5));
        var delay = TimeSpan.FromSeconds(pollSeconds);

        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);   // let the host finish starting

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A worker must never die on a bad row.
                _logger.LogError(ex, "API Configurator outbox poll failed. Retrying after the next interval.");
            }

            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<ApiConfigDataAccess>();
        var publisher = scope.ServiceProvider.GetRequiredService<ApiConfigQueuePublisher>();
        var engine = scope.ServiceProvider.GetRequiredService<ApiConfigExecutionEngine>();

        var pending = db.Table("dbo.API_Config_Outbox_GetPending", ApiConfigDataAccess.P("@Top", 10));
        if (pending.Rows.Count == 0) return;

        foreach (DataRow row in pending.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outboxId = ApiConfigDataAccess.Int(row, "OutboxId");
            var executionId = ApiConfigDataAccess.Int(row, "ExecutionId");
            var payload = ApiConfigDataAccess.Str(row, "PayloadJson");

            try
            {
                var publishedToQueue = await publisher.PublishAsync(executionId, payload, cancellationToken);

                if (!publishedToQueue)
                {
                    var outcome = await engine.ProcessAsync(executionId, cancellationToken);
                    _logger.LogInformation("Execution {ExecutionId} processed in process: {Outcome}", executionId, outcome);
                }

                db.Execute("dbo.API_Config_Outbox_MarkPublished",
                    ApiConfigDataAccess.P("@OutboxId", outboxId),
                    ApiConfigDataAccess.P("@Succeeded", true),
                    ApiConfigDataAccess.P("@Error", DBNull.Value));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox message {OutboxId} failed for execution {ExecutionId}.", outboxId, executionId);

                db.Execute("dbo.API_Config_Outbox_MarkPublished",
                    ApiConfigDataAccess.P("@OutboxId", outboxId),
                    ApiConfigDataAccess.P("@Succeeded", false),
                    ApiConfigDataAccess.P("@Error", ex.Message.Length > 1900 ? ex.Message[..1900] : ex.Message));
            }
        }
    }
}

/// <summary>
/// One-line registration so Program.cs needs a single added statement rather
/// than a block of wiring.
/// </summary>
public static class ApiConfigModule
{
    public static IServiceCollection AddApiConfigurator(this IServiceCollection services)
    {
        services.AddSingleton<ApiConfigDataAccess>();
        services.AddSingleton<ApiConfigSecretResolver>();
        services.AddSingleton<ApiConfigQueuePublisher>();
        services.AddSingleton<ApiConfigExecutionEngine>();

        services.AddHttpClient(ApiConfigExecutionEngine.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,          // never follow a redirect to an unvalidated host
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
                });

        services.AddHostedService<ApiConfigExecutionWorker>();

        services.AddSingleton<ApiPipelineEngine>();
        services.AddHostedService<ApiPipelineWorker>();
        return services;
    }
}
