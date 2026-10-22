using System.Text.Json;
using Amazon;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;

namespace AgenticPitstop.Infrastructure;

/// <summary>
/// Where the database connection string comes from.
///
/// With Provider = "None" the application reads appsettings.json exactly as it
/// always has, so every existing environment is untouched. With a provider set,
/// the secret is fetched once at startup and pushed into IConfiguration, which
/// means no other class in the portal changes: ApsDb and the secret resolver
/// still call GetConnectionString("ApsDatabase") and simply receive the value
/// the cloud returned.
///
/// Adding Azure Key Vault or GCP Secret Manager later means writing one class
/// and adding it to Stores - nothing else is edited. One JSON block per
/// concern, the same approach as ArchiveJson and DriverJson elsewhere.
/// </summary>
public sealed class ApsSecretSettings
{
    /// <summary>None | AWS | AZURE | GCP</summary>
    public string Provider { get; set; } = "None";

    /// <summary>AWS region, Key Vault URI, or GCP project - whatever the store needs.</summary>
    public string Region { get; set; }

    /// <summary>The secret's name or id in the store.</summary>
    public string SecretName { get; set; }

    /// <summary>
    /// The property to take when the secret holds a JSON object. Leave empty
    /// when the secret value is the connection string itself.
    /// </summary>
    public string SecretKey { get; set; }

    /// <summary>Which entry under ConnectionStrings to fill.</summary>

    public string ConnectionStringKey { get; set; } = "ApsDatabase";

    /// <summary>
    /// Base64 ciphertext for the AWS_KMS provider. Ignored by every other
    /// provider, because Secrets Manager holds its own ciphertext.
    /// </summary>
    public string CipherText { get; set; }

    /// <summary>
    /// Optional. When set, the ciphertext must have been encrypted under this
    /// key or the decrypt is refused - a guard against a secret being re-keyed
    /// without anyone noticing.
    /// </summary>
    public string KmsKeyId { get; set; }
}

public interface IApsSecretStore
{
    /// <summary>Matched against Secrets:Provider, case-insensitively.</summary>
    string Provider { get; }

    Task<string> ReadAsync(ApsSecretSettings settings, CancellationToken cancellationToken);
}

/// <summary>
/// AWS Secrets Manager. No keys appear here: the SDK's default credential chain
/// finds the IAM role on the server, or the developer's local profile - the same
/// rule the S3 archive store follows.
/// </summary>
internal sealed class AwsSecretStore : IApsSecretStore
{
    public string Provider => "AWS";

    public async Task<string> ReadAsync(ApsSecretSettings settings, CancellationToken cancellationToken)
    {
        var config = new AmazonSecretsManagerConfig();

        if (!string.IsNullOrWhiteSpace(settings.Region))
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(settings.Region.Trim());

        using var client = new AmazonSecretsManagerClient(config);

        var response = await client.GetSecretValueAsync(
            new GetSecretValueRequest { SecretId = settings.SecretName.Trim() },
            cancellationToken);

        return response.SecretString;
    }
}
/// <summary>
/// AWS KMS, decrypting a ciphertext carried in configuration rather than held in
/// Secrets Manager. Useful only where Secrets Manager is not available - with it,
/// KMS is already applied and this class is unnecessary.
///
/// No keys here either: the default credential chain supplies them, and the key
/// id is carried inside the ciphertext, so nothing identifying is stored twice.
/// </summary>
internal sealed class AwsKmsSecretStore : IApsSecretStore
{
    public string Provider => "AWS_KMS";

    public async Task<string> ReadAsync(ApsSecretSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.CipherText))
            throw new InvalidOperationException(
                "Secrets:CipherText is required when Secrets:Provider is 'AWS_KMS'.");

        var config = new Amazon.KeyManagementService.AmazonKeyManagementServiceConfig();
        if (!string.IsNullOrWhiteSpace(settings.Region))
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(settings.Region.Trim());

        using var client = new Amazon.KeyManagementService.AmazonKeyManagementServiceClient(config);

        using var blob = new MemoryStream(Convert.FromBase64String(settings.CipherText.Trim()));

        var request = new Amazon.KeyManagementService.Model.DecryptRequest { CiphertextBlob = blob };
        if (!string.IsNullOrWhiteSpace(settings.KmsKeyId))
            request.KeyId = settings.KmsKeyId.Trim();

        var response = await client.DecryptAsync(request, cancellationToken);

        return System.Text.Encoding.UTF8.GetString(response.Plaintext.ToArray());
    }
}

public static class ApsSecrets
{
    /// <summary>
    /// The stores this build knows about. Add a class here to support another
    /// cloud; nothing else in the portal needs touching.
    ///
    /// Azure:  add package Azure.Security.KeyVault.Secrets, then a class whose
    ///         Provider is "AZURE" and whose ReadAsync calls
    ///         new SecretClient(new Uri(settings.Region), new DefaultAzureCredential())
    ///             .GetSecretAsync(settings.SecretName).
    /// GCP:    add package Google.Cloud.SecretManager.V1, then a class whose
    ///         Provider is "GCP" and whose ReadAsync calls
    ///         SecretManagerServiceClient.Create().AccessSecretVersion(...).
    /// </summary>
    public static readonly List<IApsSecretStore> Stores = new()
    {
        new AwsSecretStore(),
        new AwsKmsSecretStore()
    };

    /// <summary>
    /// Called once, before the host is built. Startup fails loudly if a provider
    /// is configured but cannot be read - a portal that silently fell back to a
    /// stale local password would be worse than one that does not start.
    /// </summary>
    /// <summary>Set when the secret could not be read. The app starts anyway and
    /// shows this on every page, instead of failing with a 500.30 nobody can read.</summary>
    public static string StartupError { get; private set; }

    public static void Apply(ConfigurationManager configuration)
    {
        try { ApplyCore(configuration); }
        catch (Exception ex) { StartupError = ex.Message; }
    }

    private static void ApplyCore(ConfigurationManager configuration)
    {
        var settings = new ApsSecretSettings();
        configuration.GetSection("Secrets").Bind(settings);

        var provider = (settings.Provider ?? string.Empty).Trim();

        // Nothing configured: behave exactly as before.
        if (provider.Length == 0 || provider.Equals("None", StringComparison.OrdinalIgnoreCase))
            return;

        var store = Stores.FirstOrDefault(s =>
                        string.Equals(s.Provider, provider, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Secrets:Provider is '{provider}', but no secret store is registered for it. " +
                $"Registered providers: {string.Join(", ", Stores.Select(s => s.Provider))}.");

        if (string.IsNullOrWhiteSpace(settings.SecretName))
            throw new InvalidOperationException(
                "Secrets:SecretName is required when Secrets:Provider is set.");

        string payload;
        try
        {
            payload = store.ReadAsync(settings, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // Startup still fails - a portal with no connection string cannot do
            // anything - but the message says which thing is wrong and what to do
            // about it, rather than leaving an SDK error on the screen.
            throw new InvalidOperationException(Describe(store, settings, ex), ex);
        }

        var connectionString = Extract(payload, settings.SecretKey);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                $"The secret '{settings.SecretName}' did not contain a connection string" +
                (string.IsNullOrWhiteSpace(settings.SecretKey)
                    ? "."
                    : $" under '{settings.SecretKey}'."));

        // More than one name may point at the same database: ApsDb reads
        // ConnectionStrings:ApsDatabase, ApiConfigDataAccess reads
        // ConnectionStrings:AgenticPitstop. Both are filled from the one secret.
        var keys = (string.IsNullOrWhiteSpace(settings.ConnectionStringKey)
                ? "ApsDatabase"
                : settings.ConnectionStringKey)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var values = new Dictionary<string, string>();
        foreach (var name in keys)
            values["ConnectionStrings:" + name] = connectionString;

        // Added last, so it wins over appsettings.json without rewriting the file.
        configuration.AddInMemoryCollection(values);
    }

    /// <summary>
    /// A secret is either the connection string itself, or a JSON object holding
    /// it. Both are accepted, so the store can be shared with other teams.
    /// </summary>
    private static string Extract(string payload, string secretKey)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;

        if (!payload.TrimStart().StartsWith("{", StringComparison.Ordinal))
            return payload.Trim();

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (!string.IsNullOrWhiteSpace(secretKey))
            return root.TryGetProperty(secretKey.Trim(), out var named) ? named.GetString() : null;

        // No key named: a single-property secret is unambiguous.
        foreach (var property in root.EnumerateObject())
            return property.Value.GetString();

        return null;
    }
    /// <summary>
    /// Plain-language cause and remedy for a secret-store failure. Anything not
    /// recognised falls through to the provider's own message, so nothing is lost.
    /// </summary>
    private static string Describe(IApsSecretStore store, ApsSecretSettings settings, Exception ex)
    {
        var where = $"{store.Provider} secret '{settings.SecretName}' in region " +
                    $"'{settings.Region ?? "(not set)"}'";

        // The SDK wraps its real error, so walk the chain for the service fault.
        var service = Unwrap<Amazon.Runtime.AmazonServiceException>(ex);

        if (service != null)
        {
            var code = service.ErrorCode ?? string.Empty;

            if (code.Contains("ExpiredToken", StringComparison.OrdinalIgnoreCase) ||
                code.Equals("RequestExpired", StringComparison.OrdinalIgnoreCase))
                return $"Your AWS session has expired, so {where} could not be read. " +
                       "Run 'aws configure' again with a fresh access key, secret key and " +
                       "session token, then restart the application. On a server, attach an " +
                       "IAM role instead so the credentials renew themselves.";

            if (code.Contains("UnrecognizedClient", StringComparison.OrdinalIgnoreCase) ||
                code.Contains("InvalidClientTokenId", StringComparison.OrdinalIgnoreCase) ||
                code.Contains("InvalidSignature", StringComparison.OrdinalIgnoreCase))
                return $"The AWS credentials on this machine were rejected, so {where} " +
                       "could not be read. Check the access key and secret key, and that " +
                       "they belong to the same account as the secret.";

            if (code.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) ||
                code.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
                return $"Access to {where} was denied. The IAM role or user needs " +
                       "'secretsmanager:GetSecretValue' on that secret, and 'kms:Decrypt' " +
                       "on the KMS key protecting it.";

            if (code.Contains("ResourceNotFound", StringComparison.OrdinalIgnoreCase))
                return $"No secret named '{settings.SecretName}' exists in region " +
                       $"'{settings.Region}'. Check Secrets:SecretName and Secrets:Region - " +
                       "a secret is only visible in the region it was created in.";

            if (code.Contains("DecryptionFailure", StringComparison.OrdinalIgnoreCase))
                return $"{where} could not be decrypted. The KMS key it was encrypted with " +
                       "may be disabled, scheduled for deletion, or not permitted to this role.";

            if (code.Contains("Throttling", StringComparison.OrdinalIgnoreCase) ||
                code.Contains("LimitExceeded", StringComparison.OrdinalIgnoreCase))
                return $"AWS is throttling requests, so {where} could not be read. " +
                       "Wait a moment and restart the application.";

            if ((int)service.StatusCode >= 500)
                return $"AWS reported a service error while reading {where}. " +
                       "This is usually temporary - restart the application and try again.";
        }

        // No service response at all: credentials missing, or the network is blocked.
        var client = Unwrap<Amazon.Runtime.AmazonClientException>(ex);
        if (client != null)
            return $"{where} could not be reached. Either no AWS credentials were found on " +
                   "this machine - run 'aws configure', or attach an IAM role on a server - " +
                   "or outbound HTTPS to AWS is blocked by the network.";

        return $"The connection string could not be read from {where}: {ex.Message}";
    }

    /// <summary>First exception of this type in the chain, or null.</summary>
    private static T Unwrap<T>(Exception ex) where T : Exception
    {
        for (var current = ex; current != null; current = current.InnerException)
            if (current is T match) return match;

        return null;
    }
}