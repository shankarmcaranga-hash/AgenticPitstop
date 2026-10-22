using System.Data;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace AgenticPitstop.Infrastructure;

/// <summary>
/// ADO.NET access for the API Configurator. Every call goes through an
/// API_Config_ stored procedure - no ORM and no inline SQL, matching the rest
/// of the portal.
///
/// This is separate from ApsDb on purpose: the configurator owns its own
/// API_Config_ schema, so mixing the two would blur which procedures belong to
/// which module. Both read the same connection string.
/// </summary>
public sealed class ApiConfigDataAccess
{
    private readonly string _connectionString;

    public ApiConfigDataAccess(IConfiguration configuration)
    {
        // CHECK THIS LINE. Set ApiConfigurator:ConnectionStringName in
        // appsettings.json to whatever name ApsDb already uses. If the name is
        // absent the first configured connection string is used, which keeps a
        // fresh deployment working without editing code.
        var name = configuration["ApiConfigurator:ConnectionStringName"] ?? "AgenticPitstop";

        _connectionString = configuration.GetConnectionString(name)
                            ?? configuration.GetSection("ConnectionStrings")
                                            .GetChildren()
                                            .Select(c => c.Value)
                                            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
                            ?? throw new InvalidOperationException(
                                $"No connection string found. Add ConnectionStrings:{name} to appsettings.json.");
    }

    public static SqlParameter P(string name, object value) => new(name, value ?? DBNull.Value);

    public static SqlParameter Out(string name, SqlDbType type, int size = 0)
    {
        var p = new SqlParameter(name, type) { Direction = ParameterDirection.Output };
        if (size > 0) p.Size = size;
        return p;
    }

    public DataTable Table(string procedureName, params SqlParameter[] parameters)
    {
        var set = DataSet(procedureName, parameters);
        return set.Tables.Count > 0 ? set.Tables[0] : new DataTable();
    }

        public DataSet DataSet(string procedureName, params SqlParameter[] parameters)
        {
            using var connection = new SqlConnection(_connectionString);
            using var command = NewCommand(connection, procedureName, parameters);
            using var adapter = new SqlDataAdapter(command);

            var set = new System.Data.DataSet();
            adapter.Fill(set);
            return set;
    }

    public int Execute(string procedureName, params SqlParameter[] parameters)
    {
        using var connection = new SqlConnection(_connectionString);
        using var command = NewCommand(connection, procedureName, parameters);
        connection.Open();
        return command.ExecuteNonQuery();
    }

    private static SqlCommand NewCommand(SqlConnection connection, string procedureName, SqlParameter[] parameters)
    {
        var command = new SqlCommand(procedureName, connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 60
        };

        if (parameters is { Length: > 0 })
            command.Parameters.AddRange(parameters);

        return command;
    }

    // -------------------------------------------------------------------------
    // Readers, so views and controllers never repeat DBNull handling
    // -------------------------------------------------------------------------
    public static string Str(DataRow row, string column)
    {
        if (row == null || !row.Table.Columns.Contains(column) || row[column] == DBNull.Value)
            return string.Empty;
        return Convert.ToString(row[column]) ?? string.Empty;
    }

    public static int Int(DataRow row, string column, int fallback = 0)
    {
        if (row == null || !row.Table.Columns.Contains(column) || row[column] == DBNull.Value)
            return fallback;
        return Convert.ToInt32(row[column]);
    }

    public static bool Bool(DataRow row, string column, bool fallback = false)
    {
        if (row == null || !row.Table.Columns.Contains(column) || row[column] == DBNull.Value)
            return fallback;
        return Convert.ToBoolean(row[column]);
    }

    public static DateTime? Date(DataRow row, string column)
    {
        if (row == null || !row.Table.Columns.Contains(column) || row[column] == DBNull.Value)
            return null;
        return Convert.ToDateTime(row[column]);
    }

    public static DataRow First(DataTable table) => table is { Rows.Count: > 0 } ? table.Rows[0] : null;

    public static string ShortHash(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash)) return "-";
        return hash.Length <= 16 ? hash : hash[..8] + "..." + hash[^8..];
    }
}

/// <summary>
/// Resolves {{Placeholder}} tokens against a source JSON document.
///
/// Deliberately limited to dotted property paths and a small System.* set.
/// No expressions and no template engine - connector configuration must never
/// become executable code.
/// </summary>
public static class ApiConfigPayloadResolver
{
    private static readonly Regex Placeholder =
        new(@"\{\{\s*([A-Za-z0-9_\.]+)\s*\}\}", RegexOptions.Compiled);

    public static string Resolve(string template, string sourceJson, string executionUid, out List<string> unresolved)
    {
        unresolved = new List<string>();
        if (string.IsNullOrWhiteSpace(template)) return string.Empty;

        JsonNode source = null;
        if (!string.IsNullOrWhiteSpace(sourceJson))
        {
            try { source = JsonNode.Parse(sourceJson); }
            catch (JsonException) { source = null; }
        }

        var missing = new List<string>();

        var resolved = Placeholder.Replace(template, match =>
        {
            var path = match.Groups[1].Value;

            if (path.StartsWith("System.", StringComparison.OrdinalIgnoreCase))
            {
                var systemValue = path.ToLowerInvariant() switch
                {
                    "system.executionid" => executionUid,
                    "system.utcnow" => DateTime.UtcNow.ToString("o"),
                    _ => null
                };

                if (systemValue != null) return systemValue;
                missing.Add(path);
                return match.Value;
            }

            var value = ReadPath(source, path);
            if (value == null)
            {
                missing.Add(path);
                return match.Value;
            }

            return value;
        });

        unresolved = missing.Distinct().ToList();
        return resolved;
    }

    private static string ReadPath(JsonNode source, string dottedPath)
    {
        if (source == null) return null;

        JsonNode current = source;
        foreach (var segment in dottedPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out current) || current == null)
                return null;
        }

        return current is JsonValue value ? value.ToString() : current.ToJsonString();
    }

    public static string Pretty(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return json;
        }
    }

    public static bool IsValidJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return true;   // empty is allowed, malformed is not
        try { using var _ = JsonDocument.Parse(json); return true; }
        catch (JsonException) { return false; }
    }

    /// <summary>
    /// Builds the absolute target URL. It always comes from published
    /// configuration, never from transaction data.
    /// </summary>
    public static string BuildUrl(string baseUrl, string endpointTemplate, DataTable queryParameters,
                                  string sourceJson, string executionUid, out List<string> unresolved)
    {
        var path = Resolve(endpointTemplate ?? string.Empty, sourceJson, executionUid, out unresolved);

        var builder = new StringBuilder((baseUrl ?? string.Empty).TrimEnd('/'));
        builder.Append(path.StartsWith('/') ? path : "/" + path);

        if (queryParameters is { Rows.Count: > 0 })
        {
            var first = true;
            foreach (DataRow row in queryParameters.Rows)
            {
                var name = ApiConfigDataAccess.Str(row, "ParameterName");
                if (string.IsNullOrWhiteSpace(name)) continue;

                var value = Resolve(ApiConfigDataAccess.Str(row, "ParameterValueTemplate"),
                                    sourceJson, executionUid, out var queryMissing);
                unresolved.AddRange(queryMissing);

                builder.Append(first ? '?' : '&');
                builder.Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value));
                first = false;
            }
        }

        unresolved = unresolved.Distinct().ToList();
        return builder.ToString();
    }
}

/// <summary>
/// Server-side configuration validation and connection testing. Results come
/// back as DataTables so the view renders them like every other result set.
/// </summary>
public static class ApiConfigChecks
{
    public static DataTable NewResultTable()
    {
        var table = new DataTable();
        table.Columns.Add("Severity");
        table.Columns.Add("Field");
        table.Columns.Add("Message");
        return table;
    }

    private static void Add(DataTable table, string severity, string field, string message)
    {
        var row = table.NewRow();
        row["Severity"] = severity;
        row["Field"] = field;
        row["Message"] = message;
        table.Rows.Add(row);
    }

    public static DataTable Validate(DataRow version, DataTable headers, DataTable queryParameters,
                                     DataTable allowedHosts, DataTable successCodes, DataRow auth,
                                     bool allowNonHttpsTargets)
    {
        var results = NewResultTable();
        if (version == null)
        {
            Add(results, "Error", "Connector", "The connector version could not be loaded.");
            return results;
        }

        string V(string c) => ApiConfigDataAccess.Str(version, c);

        // --- step 1: identity -------------------------------------------------
        if (string.IsNullOrWhiteSpace(V("ConnectorName")))
            Add(results, "Error", "Connector Name", "A connector name is required.");

        if (string.IsNullOrWhiteSpace(V("ConnectorCode")))
            Add(results, "Error", "Connector Code", "A stable connector code is required.");

        var operation = V("OperationCode");
        if (string.IsNullOrWhiteSpace(operation))
            Add(results, "Error", "Operation Code", "An operation code is required.");
        else if (!operation.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_'))
            Add(results, "Error", "Operation Code", "Use uppercase letters, digits and underscore only, for example RATE_PLAN_UPDATE.");

        var protocol = V("Protocol");
        if (!string.Equals(protocol, "REST", StringComparison.OrdinalIgnoreCase))
            Add(results, "Error", "Protocol", $"{protocol} has no executor in this release. Only REST can be published.");

        // --- step 2: endpoint and routing ------------------------------------
        var method = V("HttpMethod");
        if (string.IsNullOrWhiteSpace(method))
            Add(results, "Error", "HTTP Method", "An HTTP method is required.");

        var baseUrl = V("BaseUrl");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            Add(results, "Error", "Base URL", "A base URL is required.");
        }
        else if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsedBase))
        {
            Add(results, "Error", "Base URL", "The base URL is not a valid absolute URL.");
        }
        else
        {
            if (!allowNonHttpsTargets && parsedBase.Scheme != Uri.UriSchemeHttps)
                Add(results, "Error", "Base URL", "The base URL must use HTTPS.");

            // A path here plus a path in the template produces a doubled URL and
            // a 404 that is hard to spot on the approval screen.
            if (!string.IsNullOrWhiteSpace(parsedBase.AbsolutePath.Trim('/')))
                Add(results, "Error", "Base URL",
                    $"The base URL must be scheme and host only. Move '/{parsedBase.AbsolutePath.Trim('/')}' into the endpoint template.");

            var hostAllowed = allowedHosts != null && allowedHosts.Rows.Cast<DataRow>()
                .Any(r => string.Equals(ApiConfigDataAccess.Str(r, "HostName"), parsedBase.Host, StringComparison.OrdinalIgnoreCase));

            if (!hostAllowed)
                Add(results, "Error", "Approved Hosts", $"The base URL host '{parsedBase.Host}' is not in the approved host allowlist.");
        }

        var endpoint = V("EndpointTemplate");
        if (string.IsNullOrWhiteSpace(endpoint))
            Add(results, "Error", "Endpoint Template", "A relative endpoint template is required.");
        else if (!endpoint.StartsWith('/'))
            Add(results, "Error", "Endpoint Template", "The endpoint template must start with a forward slash.");
        else if (endpoint.Contains("://"))
            Add(results, "Error", "Endpoint Template", "The endpoint template must not contain a scheme or host.");

        if (allowedHosts == null || allowedHosts.Rows.Count == 0)
            Add(results, "Error", "Approved Hosts", "At least one approved host is required before publishing.");

        var restricted = new[] { "host", "content-length", "connection", "transfer-encoding", "upgrade", "keep-alive" };
        if (headers != null)
        {
            foreach (DataRow header in headers.Rows)
            {
                var name = ApiConfigDataAccess.Str(header, "HeaderName");
                if (string.IsNullOrWhiteSpace(name)) continue;

                if (restricted.Contains(name.ToLowerInvariant()))
                    Add(results, "Error", "Headers", $"'{name}' is a restricted header and cannot be configured.");

                if (LooksLikeSecret(ApiConfigDataAccess.Str(header, "HeaderValueTemplate")))
                    Add(results, "Error", "Headers", $"Header '{name}' looks like it contains a literal credential. Store a secret reference instead.");
            }
        }

        if (queryParameters != null)
        {
            foreach (DataRow parameter in queryParameters.Rows)
            {
                if (LooksLikeSecret(ApiConfigDataAccess.Str(parameter, "ParameterValueTemplate")))
                    Add(results, "Warning", "Query Parameters",
                        $"Parameter '{ApiConfigDataAccess.Str(parameter, "ParameterName")}' may carry sensitive data in the URL. Prefer a header.");
            }
        }

        // --- step 3: authentication ------------------------------------------
        var authType = auth == null ? "None" : ApiConfigDataAccess.Str(auth, "AuthType");
        switch (authType)
        {
            case "None":
                Add(results, "Warning", "Authentication", "This connector calls an unauthenticated endpoint. Confirm that this is approved.");
                break;

            case "OAuth2ClientCredentials":
                RequireRef(results, auth, "ClientIdSecretRef", "Client ID secret reference");
                RequireRef(results, auth, "ClientSecretSecretRef", "Client secret reference");
                if (string.IsNullOrWhiteSpace(ApiConfigDataAccess.Str(auth, "TokenEndpoint")))
                    Add(results, "Error", "Authentication", "A token endpoint is required for OAuth2 client credentials.");
                break;

            case "ApiKey":
                RequireRef(results, auth, "ApiKeySecretRef", "API key secret reference");
                if (string.IsNullOrWhiteSpace(ApiConfigDataAccess.Str(auth, "ApiKeyName")))
                    Add(results, "Error", "Authentication", "An API key name is required.");
                if (string.Equals(ApiConfigDataAccess.Str(auth, "ApiKeyLocation"), "Query", StringComparison.OrdinalIgnoreCase))
                    Add(results, "Warning", "Authentication", "Sending an API key in the query string is discouraged. Use a header.");
                break;

            case "BearerToken": RequireRef(results, auth, "BearerTokenSecretRef", "Bearer token secret reference"); break;

            case "Basic":
                RequireRef(results, auth, "UsernameSecretRef", "Username secret reference");
                RequireRef(results, auth, "PasswordSecretRef", "Password secret reference");
                break;

            case "ClientCertificate": RequireRef(results, auth, "CertificateSecretRef", "Certificate secret reference"); break;
        }

        if (auth != null && authType != "None" && authType != "WorkloadIdentity"
            && string.IsNullOrWhiteSpace(ApiConfigDataAccess.Str(auth, "SecretStoreAlias")))
            Add(results, "Error", "Authentication", "A secret store alias is required when the connector uses credentials.");

        // --- step 4: mapping --------------------------------------------------
        var template = V("PayloadTemplateJson");
        if (string.IsNullOrWhiteSpace(template))
        {
            if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase))
                Add(results, "Error", "Payload Template", "A payload template is required for this HTTP method.");
        }
        else if (!ApiConfigPayloadResolver.IsValidJson(template))
        {
            Add(results, "Error", "Payload Template", "The payload template is not valid JSON.");
        }
        else
        {
            var sample = V("SampleSourceJson");
            if (!string.IsNullOrWhiteSpace(sample))
            {
                ApiConfigPayloadResolver.Resolve(template, sample, "00000000-0000-0000-0000-000000000000", out var unresolved);
                foreach (var path in unresolved)
                    Add(results, "Warning", "Payload Template",
                        $"Placeholder {{{{{path}}}}} does not resolve against the sample source data.");
            }
            else
            {
                Add(results, "Warning", "Sample Source Data", "No sample source data is saved, so placeholder resolution cannot be checked.");
            }
        }

        if (!string.IsNullOrWhiteSpace(V("SampleSourceJson")) && !ApiConfigPayloadResolver.IsValidJson(V("SampleSourceJson")))
            Add(results, "Error", "Sample Source Data", "The sample source data is not valid JSON.");

        // --- step 5: policies -------------------------------------------------
        if (successCodes == null || successCodes.Rows.Count == 0)
            Add(results, "Error", "Success Codes", "At least one success status code is required.");

        var timeout = ApiConfigDataAccess.Int(version, "TimeoutSeconds", 30);
        if (timeout is < 1 or > 120)
            Add(results, "Error", "Timeout", "Timeout must be between 1 and 120 seconds.");

        var attempts = ApiConfigDataAccess.Int(version, "MaxAttempts", 1);
        if (attempts is < 1 or > 5)
            Add(results, "Error", "Maximum Attempts", "Maximum attempts must be between 1 and 5.");

        var unsafeMethod = method is "POST" or "PATCH";
        if (attempts > 1 && unsafeMethod && !ApiConfigDataAccess.Bool(version, "IsRetrySafe"))
            Add(results, "Error", "Retry Policy",
                $"{method} cannot be retried automatically unless the target guarantees idempotency. Set Retry Safe, or reduce attempts to 1.");

        if (ApiConfigDataAccess.Bool(version, "IsRetrySafe") && string.IsNullOrWhiteSpace(V("IdempotencyHeader")))
            Add(results, "Warning", "Retry Policy", "Retry Safe is set but no idempotency header is configured.");

        var mapping = V("ResultMappingJson");
        if (!string.IsNullOrWhiteSpace(mapping) && !ApiConfigPayloadResolver.IsValidJson(mapping))
            Add(results, "Error", "Business Result Mapping", "The result mapping is not valid JSON.");

        if (results.Rows.Count == 0)
            Add(results, "Info", "Configuration", "All structural and security checks passed.");

        return results;
    }

    private static void RequireRef(DataTable results, DataRow auth, string column, string label)
    {
        var value = ApiConfigDataAccess.Str(auth, column);
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(results, "Error", "Authentication", $"{label} is required.");
            return;
        }

        if (!value.StartsWith("secret://", StringComparison.OrdinalIgnoreCase))
            Add(results, "Warning", "Authentication",
                $"{label} does not look like a secret reference. Expected the form secret://scope/name.");

        if (LooksLikeSecret(value))
            Add(results, "Error", "Authentication", $"{label} appears to hold a literal credential. Store only a reference.");
    }

    /// <summary>Catches a pasted key or token before it reaches SQL.</summary>
    private static bool LooksLikeSecret(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.StartsWith("secret://", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Contains("{{")) return false;

        if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.StartsWith("eyJ", StringComparison.Ordinal)) return true;                 // JWT
        if (value.StartsWith("AKIA", StringComparison.Ordinal)) return true;                // AWS access key id

        var looksRandom = value.Length >= 24 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '+' or '/' or '=');
        return looksRandom && value.Any(char.IsDigit) && value.Any(char.IsAsciiLetterUpper);
    }

    // -------------------------------------------------------------------------
    // Connection test. Backend only, and the configured business operation is
    // never invoked as a probe.
    // -------------------------------------------------------------------------
    public static async Task<DataTable> TestConnectionAsync(DataRow version, DataTable allowedHosts, DataRow auth,
                                                            ApiConfigSecretResolver secrets, CancellationToken cancellationToken)
    {
        var steps = new DataTable();
        steps.Columns.Add("Name");
        steps.Columns.Add("Status");
        steps.Columns.Add("Message");

        void Step(string name, string status, string message)
        {
            var row = steps.NewRow();
            row["Name"] = name;
            row["Status"] = status;
            row["Message"] = message;
            steps.Rows.Add(row);
        }

        var baseUrl = ApiConfigDataAccess.Str(version, "BaseUrl");
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var target))
        {
            Step("DestinationValidation", "Failed", "The base URL is not a valid absolute URL.");
            return steps;
        }

        var allowed = allowedHosts != null && allowedHosts.Rows.Cast<DataRow>()
            .Any(r => string.Equals(ApiConfigDataAccess.Str(r, "HostName"), target.Host, StringComparison.OrdinalIgnoreCase));

        if (!allowed)
        {
            Step("DestinationValidation", "Failed", $"Host '{target.Host}' is not in the approved allowlist. No connection was attempted.");
            return steps;
        }
        if (target.Scheme != Uri.UriSchemeHttps)
        {
            Step("DestinationValidation", "Failed", "The target is not HTTPS. No connection was attempted.");
            return steps;
        }
        Step("DestinationValidation", "Passed", $"Host '{target.Host}' is allowlisted and uses HTTPS.");

        System.Net.IPAddress[] addresses;
        try
        {
            addresses = await System.Net.Dns.GetHostAddressesAsync(target.Host, cancellationToken);
            if (addresses.Length == 0)
            {
                Step("Dns", "Failed", "The hostname did not resolve to any address.");
                return steps;
            }
            Step("Dns", "Passed", $"Resolved to {addresses.Length} address(es).");
        }
        catch (Exception ex)
        {
            Step("Dns", "Failed", Redact(ex.Message));
            return steps;
        }

        try
        {
            using var tcp = new TcpClient();
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(10));

            await tcp.ConnectAsync(target.Host, target.Port, timeoutSource.Token);

            await using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = target.Host },
                                                timeoutSource.Token);

            Step("Tls", "Passed", $"Handshake completed using {ssl.SslProtocol}. Server certificate validated by the OS trust store.");
        }
        catch (Exception ex)
        {
            Step("Tls", "Failed", Redact(ex.Message));
            return steps;
        }

        var authType = auth == null ? "None" : ApiConfigDataAccess.Str(auth, "AuthType");
        if (authType == "None")
        {
            Step("SecretResolution", "Skipped", "This connector is configured without credentials.");
            Step("Authentication", "Skipped", "No authentication is configured.");
        }
        else
        {
            var references = new[]
            {
                ApiConfigDataAccess.Str(auth, "ClientIdSecretRef"), ApiConfigDataAccess.Str(auth, "ClientSecretSecretRef"),
                ApiConfigDataAccess.Str(auth, "ApiKeySecretRef"), ApiConfigDataAccess.Str(auth, "BearerTokenSecretRef"),
                ApiConfigDataAccess.Str(auth, "UsernameSecretRef"), ApiConfigDataAccess.Str(auth, "PasswordSecretRef"),
                ApiConfigDataAccess.Str(auth, "CertificateSecretRef")
            }.Where(r => !string.IsNullOrWhiteSpace(r)).ToArray();

            var resolvedCount = 0;
            string failure = null;

            foreach (var reference in references)
            {
                try
                {
                    var value = await secrets.ResolveAsync(ApiConfigDataAccess.Str(auth, "SecretStoreAlias"), reference, cancellationToken);
                    if (!string.IsNullOrEmpty(value)) resolvedCount++;
                }
                catch (Exception ex)
                {
                    failure = Redact(ex.Message);
                    break;
                }
            }

            var allResolved = failure == null && references.Length > 0 && resolvedCount == references.Length;

            if (failure != null)
                Step("SecretResolution", "Failed", failure);
            else if (allResolved)
                Step("SecretResolution", "Passed", $"{resolvedCount} secret reference(s) resolved. No value was written to SQL or returned to the browser.");
            else
                Step("SecretResolution", "Warning", "The secret provider is not configured in this environment, so references could not be resolved.");

            // Only OAuth2 exchanges anything. For the other schemes there is no
            // token to acquire, so Skipped is the correct - and honest - outcome.
            Step("Authentication", "Skipped",
                authType == "OAuth2ClientCredentials"
                    ? (allResolved ? "Token acquisition is exercised at send time, not during a connection test."
                                   : "Token acquisition is skipped unless every secret reference resolved.")
                    : $"{authType} needs no token exchange, so there is nothing to acquire.");
        }

        Step("SafeProbe", "Skipped", "No health check endpoint is configured. A POST/PUT/PATCH operation is never used as a probe.");

        return steps;
    }

    private static string Redact(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;
        return message.Length > 300 ? message[..300] : message;
    }

    public static string ToJson(DataTable table)
    {
        var rows = table.Rows.Cast<DataRow>()
            .Select(r => table.Columns.Cast<DataColumn>()
                              .ToDictionary(c => c.ColumnName, c => Convert.ToString(r[c]) ?? string.Empty))
            .ToList();

        return JsonSerializer.Serialize(rows);
    }

    public static bool HasErrors(DataTable results) =>
        results.Rows.Cast<DataRow>().Any(r => ApiConfigDataAccess.Str(r, "Severity") == "Error");
}
