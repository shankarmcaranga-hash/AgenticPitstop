using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgenticPitstop.Infrastructure;

// =============================================================================
// Where an uploaded workbook is kept
//
// The configuration screen writes one JSON document per use case; this file
// turns that document into a stored file. Adding a provider means adding one
// class and one registration line - nothing here is edited.
// =============================================================================

/// <summary>
/// The archive settings of one use case, as stored in APS_InflowConfig.ArchiveJson.
/// Every provider reads from the same object and ignores what it does not need,
/// which is why one column serves all of them.
/// </summary>
public sealed class InflowArchiveSettings
{
    public const string TargetNone = "NONE";
    public const string TargetLocal = "LOCAL";
    public const string TargetCloud = "CLOUD";

    [JsonPropertyName("target")] public string Target { get; set; } = TargetNone;
    [JsonPropertyName("provider")] public string Provider { get; set; }

    /// <summary>LOCAL: an alias from Aps:AllowedBaseFolders. Empty means the default root.</summary>
    [JsonPropertyName("root")] public string Root { get; set; }

    [JsonPropertyName("bucket")] public string Bucket { get; set; }
    [JsonPropertyName("region")] public string Region { get; set; }

    /// <summary>An optional folder or key prefix inside the root or bucket.</summary>
    [JsonPropertyName("prefix")] public string Prefix { get; set; }

    /// <summary>
    /// Optional. A reference such as secret://cwa/aws/inflow-archive, resolved at
    /// the moment of use. Left empty, the provider's own credential chain is used,
    /// which on a server means an IAM role and no stored credential at all.
    /// </summary>
    [JsonPropertyName("secretRef")] public string SecretRef { get; set; }

    /// <summary>Computed, never stored — it is derived from Target.</summary>
    [JsonIgnore]
    public bool IsEnabled => !string.Equals(Target, TargetNone, StringComparison.OrdinalIgnoreCase)
                             && !string.IsNullOrWhiteSpace(Target);

    public static InflowArchiveSettings Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new InflowArchiveSettings();

        try
        {
            return JsonSerializer.Deserialize<InflowArchiveSettings>(json)
                   ?? new InflowArchiveSettings();
        }
        catch (JsonException)
        {
            // Unreadable settings must not silently become "no archive", because
            // that would lose files without telling anyone.
            throw new InvalidOperationException("The storage settings for this use case are not readable.");
        }
    }
}

/// <summary>Where a file ended up, for the message shown to the user.</summary>
public sealed record InflowArchiveResult(string Provider, string Location);

/// <summary>
/// One place a workbook can be kept. The contract is deliberately small: validate
/// the settings, and save a stream.
/// </summary>
public interface IInflowArchiveStore
{
    /// <summary>Matches InflowArchiveSettings.Provider, or Target for the local store.</summary>
    string ProviderCode { get; }

    /// <summary>A message describing what is missing, or null when the settings are complete.</summary>
    string Validate(InflowArchiveSettings settings);

    Task<InflowArchiveResult> SaveAsync(Stream content, string fileName, string contentType,
                                        InflowArchiveSettings settings, string relativePath,
                                        CancellationToken cancellationToken);
}

// -----------------------------------------------------------------------------
// Local disk
// -----------------------------------------------------------------------------

/// <summary>
/// Writes under one of the folders an administrator has allowed. The path is
/// re-checked against those folders after it is built, so a prefix cannot walk
/// out of the permitted area.
/// </summary>
public sealed class LocalInflowArchiveStore : IInflowArchiveStore
{
    private readonly ApsFileRoots _roots;

    public LocalInflowArchiveStore(ApsFileRoots roots) => _roots = roots;

    public string ProviderCode => InflowArchiveSettings.TargetLocal;

    public string Validate(InflowArchiveSettings settings)
    {
        var root = _roots.PathFor(settings.Root);
        return string.IsNullOrWhiteSpace(root)
            ? "No folder is configured for local storage. Add one in Administration, Folder Roots."
            : null;
    }

    public Task<InflowArchiveResult> SaveAsync(Stream content, string fileName, string contentType,
                                               InflowArchiveSettings settings, string relativePath,
                                               CancellationToken cancellationToken)
    {
        var configured = _roots.PathFor(settings.Root);
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("No folder is configured for local storage.");

        // Normalised once, so a trailing separator in the configured value cannot
        // make the containment test below fail on a path that is in fact inside it.
        var root = Path.GetFullPath(configured)
                       .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var target = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The storage path resolves outside the permitted folders.");

        if (!_roots.IsInsideAllowedBase(target))
            throw new InvalidOperationException("The storage path is outside the permitted areas of this server.");

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        using (var output = File.Create(target))
            content.CopyTo(output);

        return Task.FromResult(new InflowArchiveResult("Local disk", target));
    }
}

// -----------------------------------------------------------------------------
// Amazon S3
// -----------------------------------------------------------------------------

/// <summary>
/// Puts the workbook in a bucket. The bucket and region come from the screen;
/// the credentials never do. With no secret reference configured the AWS default
/// chain is used, which on a server means an IAM role and nothing stored.
/// </summary>
public sealed class AwsS3InflowArchiveStore : IInflowArchiveStore
{
    private readonly ApiConfigSecretResolver _secrets;
    private readonly ILogger<AwsS3InflowArchiveStore> _logger;

    public AwsS3InflowArchiveStore(ApiConfigSecretResolver secrets,
                                   ILogger<AwsS3InflowArchiveStore> logger)
    {
        _secrets = secrets;
        _logger = logger;
    }

    public string ProviderCode => "AWS_S3";

    public string Validate(InflowArchiveSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Bucket)) return "Enter the S3 bucket name.";
        if (string.IsNullOrWhiteSpace(settings.Region)) return "Enter the AWS region, for example us-east-1.";

        return RegionEndpoint.GetBySystemName(settings.Region)?.SystemName == settings.Region
            ? null
            : $"'{settings.Region}' is not a known AWS region.";
    }

    public async Task<InflowArchiveResult> SaveAsync(Stream content, string fileName, string contentType,
                                                     InflowArchiveSettings settings, string relativePath,
                                                     CancellationToken cancellationToken)
    {
        using var client = await CreateClientAsync(settings, cancellationToken);

        var key = relativePath.Replace('\\', '/').TrimStart('/');

        var request = new PutObjectRequest
        {
            BucketName = settings.Bucket,
            Key = key,
            InputStream = content,
            ContentType = string.IsNullOrWhiteSpace(contentType)
                ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                : contentType
        };

        await client.PutObjectAsync(request, cancellationToken);

        _logger.LogInformation("Inflow workbook archived to s3://{Bucket}/{Key}", settings.Bucket, key);

        return new InflowArchiveResult("Amazon S3", $"s3://{settings.Bucket}/{key}");
    }

    private async Task<IAmazonS3> CreateClientAsync(InflowArchiveSettings settings,
                                                    CancellationToken cancellationToken)
    {
        var region = RegionEndpoint.GetBySystemName(settings.Region);

        if (string.IsNullOrWhiteSpace(settings.SecretRef))
            return new AmazonS3Client(region);   // IAM role, environment, or profile

        // A secret reference holds "accessKeyId:secretAccessKey". The value is
        // fetched here, used here, and never written anywhere.
        var value = await _secrets.ResolveAsync("inflow-archive", settings.SecretRef, cancellationToken);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"The secret '{settings.SecretRef}' is not configured in this environment.");

        var separator = value.IndexOf(':');
        if (separator <= 0 || separator == value.Length - 1)
            throw new InvalidOperationException("The storage secret must be in the form accessKeyId:secretAccessKey.");

        var credentials = new BasicAWSCredentials(value[..separator], value[(separator + 1)..]);
        return new AmazonS3Client(credentials, region);
    }
}

// -----------------------------------------------------------------------------
// The entry point used by the controllers
// -----------------------------------------------------------------------------

public sealed class InflowArchiveService
{
    private readonly IReadOnlyDictionary<string, IInflowArchiveStore> _stores;
    private readonly ILogger<InflowArchiveService> _logger;

    public InflowArchiveService(IEnumerable<IInflowArchiveStore> stores,
                                ILogger<InflowArchiveService> logger)
    {
        _stores = stores.ToDictionary(s => s.ProviderCode, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    /// <summary>The provider codes the screen may offer. Driven by what is registered.</summary>
    public IEnumerable<string> CloudProviders =>
        _stores.Keys.Where(k => !string.Equals(k, InflowArchiveSettings.TargetLocal,
                                               StringComparison.OrdinalIgnoreCase));

    /// <summary>Checks settings before they are saved, so a bad bucket is caught on the screen.</summary>
    public string Validate(InflowArchiveSettings settings)
    {
        if (settings == null || !settings.IsEnabled) return null;

        var store = Resolve(settings);
        return store == null
            ? "Choose where uploaded files should be stored."
            : store.Validate(settings);
    }

    /// <summary>
    /// Saves one workbook. Returns null when this use case archives nothing,
    /// which is the default and keeps existing use cases behaving as before.
    /// </summary>
    public async Task<InflowArchiveResult> ArchiveAsync(string archiveJson, int useCaseId,
                                                        Stream content, string fileName, string contentType,
                                                        CancellationToken cancellationToken)
    {
        var settings = InflowArchiveSettings.Parse(archiveJson);
        if (!settings.IsEnabled) return null;

        var store = Resolve(settings)
                    ?? throw new InvalidOperationException("The configured storage provider is not available.");

        var problem = store.Validate(settings);
        if (problem != null) throw new InvalidOperationException(problem);

        if (content.CanSeek) content.Position = 0;

        return await store.SaveAsync(content, fileName, contentType, settings,
                                     BuildPath(settings, useCaseId, fileName), cancellationToken);
    }

    private IInflowArchiveStore Resolve(InflowArchiveSettings settings)
    {
        var code = string.Equals(settings.Target, InflowArchiveSettings.TargetLocal,
                                 StringComparison.OrdinalIgnoreCase)
            ? InflowArchiveSettings.TargetLocal
            : settings.Provider;

        return string.IsNullOrWhiteSpace(code) ? null
             : _stores.TryGetValue(code, out var store) ? store : null;
    }

    /// <summary>
    /// prefix/useCase-5/20261013/20261013-093000-claims.xlsx
    ///
    /// One folder per day keeps a year of uploads browsable without three levels
    /// of nesting, and the timestamp means two files of the same name never
    /// collide inside it.
    /// </summary>
    private static string BuildPath(InflowArchiveSettings settings, int useCaseId, string fileName)
    {
        var now = DateTime.UtcNow;
        var safeName = Safe(Path.GetFileName(fileName ?? string.Empty));
        if (safeName.Length == 0) safeName = "upload.xlsx";

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.Prefix))
            parts.AddRange(settings.Prefix.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries).Select(Safe));

        parts.Add($"useCase-{useCaseId}");
        parts.Add(now.ToString("yyyyMMdd"));
        parts.Add($"{now:HHmmss}-{safeName}");

        return string.Join('/', parts.Where(p => p.Length > 0));
    }

    private static string Safe(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var cleaned = new string(value.Trim()
            .Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-')
            .ToArray());

        return cleaned.Trim('-', '.').Length == 0 ? string.Empty : cleaned.Trim('-', '.');
    }
}

/// <summary>
/// One-line registration. A new provider is added here and nowhere else.
/// </summary>
public static class InflowArchiveModule
{
    public static IServiceCollection AddInflowArchive(this IServiceCollection services)
    {
        // Scoped, because LocalInflowArchiveStore depends on ApsFileRoots, which
        // is scoped. Every store and the service itself share one lifetime so a
        // new provider cannot reintroduce this mismatch.
        services.AddScoped<IInflowArchiveStore, LocalInflowArchiveStore>();
        services.AddScoped<IInflowArchiveStore, AwsS3InflowArchiveStore>();
        // services.AddScoped<IInflowArchiveStore, AzureBlobInflowArchiveStore>();
        // services.AddScoped<IInflowArchiveStore, GcsInflowArchiveStore>();

        services.AddScoped<InflowArchiveService>();
        return services;
    }
}