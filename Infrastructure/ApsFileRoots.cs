using System.Data;

namespace AgenticPitstop.Infrastructure;

/// <summary>
/// Named folders that pipelines may write to. Admins manage them in the portal;
/// configuration only says which areas of the server are permitted at all, so a
/// folder added on the screen can never point outside them.
/// </summary>
public sealed class ApsFileRoots
{
    private readonly ApsDb _db;
    private readonly IConfiguration _configuration;

    public ApsFileRoots(ApsDb db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public sealed record Root(int Id, string Alias, string Path, string Description, bool IsDefault, bool IsActive);

    public List<Root> All(bool activeOnly = false) =>
        _db.Query("APS_FileRoot_Get", p => ApsDb.In(p, "@ActiveOnly", activeOnly ? 1 : 0))
            .AsEnumerable()
            .Select(r => new Root(
                Convert.ToInt32(r["FileRootID"]),
                ApsMetadata.Cell(r, "RootAlias"),
                ApsMetadata.Cell(r, "RootPath"),
                ApsMetadata.Cell(r, "Description"),
                ApsMetadata.Cell(r, "IsDefault") == "1",
                ApsMetadata.Cell(r, "IsActive") == "1"))
            .ToList();

    public List<string> ActiveAliases() => All(true).Select(r => r.Alias).ToList();

    /// <summary>The path for an alias, or the default folder when none is given.</summary>
    public string PathFor(string alias)
    {
        var roots = All(true);

        var hit = string.IsNullOrWhiteSpace(alias)
            ? roots.FirstOrDefault(r => r.IsDefault)
            : roots.FirstOrDefault(r => string.Equals(r.Alias, alias, StringComparison.OrdinalIgnoreCase));

        if (hit != null) return hit.Path;

        // Nothing configured on the screen yet: fall back to the built-in folder.
        return string.IsNullOrWhiteSpace(alias) ? _configuration["ApiConfigurator:CollectionFileRoot"] : null;
    }

    public List<string> AllowedBases() =>
        (_configuration.GetSection("Aps:AllowedBaseFolders").Get<string[]>() ?? Array.Empty<string>())
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Select(Normalize)
            .ToList();

    /// <summary>True when a path sits inside one of the permitted areas.</summary>
    public bool IsInsideAllowedBase(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        string full;
        try { full = Normalize(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }

        return AllowedBases().Any(b =>
            full.Equals(b, StringComparison.OrdinalIgnoreCase) ||
            full.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Every folder a saved file may legitimately live under.</summary>
    public List<string> ReadableRoots()
    {
        var roots = (_configuration.GetSection("Aps:DocumentRoots").Get<string[]>() ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .ToList();

        roots.AddRange(All(true).Select(r => r.Path));
        return roots;
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}