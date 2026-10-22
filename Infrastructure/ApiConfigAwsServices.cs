using System.Collections.Concurrent;
using System.Text.Json;
using Amazon;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using ResourceNotFoundException = Amazon.SQS.Model.ResourceNotFoundException;

namespace AgenticPitstop.Infrastructure;

/// <summary>
/// Resolves a logical secret reference such as
/// secret://cwa/gidp/client-secret into a credential value.
///
/// SQL only ever holds the reference. The value is fetched immediately before
/// it is needed, kept in memory, and never persisted or sent to the browser.
///
/// AWS Secrets Manager is the current adapter. With Aws:Enabled false the
/// resolver falls back to the LocalSecrets configuration section, which keeps
/// the portal runnable on a developer machine with no AWS account.
/// </summary>
public sealed class ApiConfigSecretResolver
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApiConfigSecretResolver> _logger;
    private readonly ConcurrentDictionary<string, (string Value, DateTime ExpiresUtc)> _cache = new();

    private IAmazonSecretsManager _client;

    public ApiConfigSecretResolver(IConfiguration configuration, ILogger<ApiConfigSecretResolver> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public bool AwsEnabled =>
        _configuration.GetValue("Aws:Enabled", false) &&
        _configuration.GetValue("Aws:SecretsManager:Enabled", false);

    public string ActiveAdapter => AwsEnabled ? "AwsSecretsManager" : "LocalConfiguration";

    public async Task<string> ResolveAsync(string storeAlias, string secretReference, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(secretReference)) return null;

        var cacheKey = storeAlias + "|" + secretReference;
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresUtc > DateTime.UtcNow)
            return cached.Value;

        var value = AwsEnabled
            ? await ResolveFromAwsAsync(secretReference, cancellationToken)
            : _configuration[$"LocalSecrets:{secretReference}"];

        if (value == null)
            _logger.LogDebug("Secret reference {Reference} is not configured in this environment.", secretReference);
        else
            _cache[cacheKey] = (value, DateTime.UtcNow.AddSeconds(_configuration.GetValue("Aws:SecretsManager:CacheSeconds", 300)));

        return value;
    }

    private async Task<string> ResolveFromAwsAsync(string secretReference, CancellationToken cancellationToken)
    {
        var path = secretReference.StartsWith("secret://", StringComparison.OrdinalIgnoreCase)
            ? secretReference["secret://".Length..]
            : secretReference;

        // The last path segment may name a key inside a JSON secret.
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var secretId = path;
        string jsonKey = null;

        if (segments.Length > 1)
        {
            secretId = string.Join('/', segments[..^1]);
            jsonKey = segments[^1];
        }

        var client = GetClient();

        GetSecretValueResponse response;
        try
        {
            response = await client.GetSecretValueAsync(new GetSecretValueRequest { SecretId = secretId }, cancellationToken);
        }
        catch (ResourceNotFoundException)
        {
            response = await client.GetSecretValueAsync(new GetSecretValueRequest { SecretId = path }, cancellationToken);
            jsonKey = null;
        }

        var secretString = response.SecretString;
        if (string.IsNullOrEmpty(secretString) || jsonKey == null) return secretString;

        try
        {
            using var document = JsonDocument.Parse(secretString);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty(jsonKey, out var element))
                return element.GetString();
        }
        catch (JsonException)
        {
            // not a JSON secret - the raw string is the value
        }

        return secretString;
    }

    private IAmazonSecretsManager GetClient()
    {
        if (_client != null) return _client;

        var region = _configuration["Aws:Region"];
        _client = string.IsNullOrWhiteSpace(region)
            ? new AmazonSecretsManagerClient()
            : new AmazonSecretsManagerClient(RegionEndpoint.GetBySystemName(region));

        return _client;
    }
}

/// <summary>
/// Publishes an approved execution to the execution transport.
///
/// The outbox row is written in the same SQL transaction as the status change;
/// this class only moves it onto the queue afterwards. With SQS disabled the
/// worker drains the outbox directly, so the whole flow runs without AWS.
///
/// SQS delivery is at-least-once, so the consumer must be idempotent. That is
/// enforced by API_Config_Execution_Claim, not here.
/// </summary>
public sealed class ApiConfigQueuePublisher
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApiConfigQueuePublisher> _logger;
    private IAmazonSQS _client;

    public ApiConfigQueuePublisher(IConfiguration configuration, ILogger<ApiConfigQueuePublisher> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public bool SqsEnabled =>
        _configuration.GetValue("Aws:Enabled", false) &&
        _configuration.GetValue("Aws:Sqs:Enabled", false) &&
        !string.IsNullOrWhiteSpace(_configuration["Aws:Sqs:QueueUrl"]);

    public string ActiveAdapter => SqsEnabled ? "AwsSqs" : "SqlOutboxDirect";

    /// <summary>
    /// True when the message reached a real queue. False means there is no
    /// external transport and the worker should execute in process.
    /// </summary>
    public async Task<bool> PublishAsync(int executionId, string payloadJson, CancellationToken cancellationToken)
    {
        if (!SqsEnabled) return false;

        var request = new SendMessageRequest
        {
            QueueUrl = _configuration["Aws:Sqs:QueueUrl"],
            MessageBody = payloadJson,
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["ExecutionId"] = new() { DataType = "String", StringValue = executionId.ToString() }
            }
        };

        await GetClient().SendMessageAsync(request, cancellationToken);
        _logger.LogInformation("API Configurator execution {ExecutionId} published to SQS.", executionId);
        return true;
    }

    private IAmazonSQS GetClient()
    {
        if (_client != null) return _client;

        var region = _configuration["Aws:Region"];
        _client = string.IsNullOrWhiteSpace(region)
            ? new AmazonSQSClient()
            : new AmazonSQSClient(RegionEndpoint.GetBySystemName(region));

        return _client;
    }
}
