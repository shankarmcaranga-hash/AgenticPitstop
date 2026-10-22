using System.Data;

namespace AgenticPitstop.Infrastructure;

/// <summary>
/// Resolves the Windows caller to a provisioned portal user and role.
///
/// The Windows identity arrives as DOMAIN\username. Only the username is stored
/// in APS_Users.portalid and written to actionby, so the domain is stripped here
/// once, at the edge, rather than in each controller.
///
/// When SSO replaces Windows Authentication, this is the only class that changes:
/// the RBAC filter, the controllers and the views all read from here.
/// </summary>
public sealed class ApsIdentity
{
    public const byte RoleAdmin = 1;
    public const byte RoleManager = 2;
    public const byte RoleAssociate = 3;

    private const string ItemsKey = "aps.currentuser";

    private readonly ApsDb _db;
    private readonly IHttpContextAccessor _http;

    public ApsIdentity(ApsDb db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    /// <summary>
    /// Elevated accounts that map to the same portal user. Inside the VM Windows
    /// reports 124323.adm; outside it reports 124323. Both are the same person,
    /// and APS_UserMaster holds only the portal id, so the suffix is dropped here.
    /// Add a suffix to this array rather than editing the method below.
    /// </summary>
    private static readonly string[] AccountSuffixes = { ".adm", ".admin", ".sa" };

    /// <summary>
    /// Strips the domain from DOMAIN\username or username@domain, and any
    /// elevated-account suffix, leaving the portal id stored in APS_UserMaster.
    /// </summary>
    public static string NormalizeUsername(string windowsName)
    {
        if (string.IsNullOrWhiteSpace(windowsName)) return string.Empty;

        var name = windowsName.Trim();

        var slash = name.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0 && slash < name.Length - 1)
            name = name[(slash + 1)..];

        var at = name.IndexOf('@');
        if (at > 0) name = name[..at];

        name = name.ToLowerInvariant();

        // Only a trailing suffix is removed, and only when something is left in
        // front of it, so a legitimate name containing a dot is untouched.
        foreach (var suffix in AccountSuffixes)
        {
            if (name.Length > suffix.Length &&
                name.EndsWith(suffix, StringComparison.Ordinal))
            {
                name = name[..^suffix.Length];
                break;
            }
        }

        return name;
    }
    //public static string NormalizeUsername(string windowsName)
    //{
    //    if (string.IsNullOrWhiteSpace(windowsName)) return string.Empty;

    //    var name = windowsName.Trim();

    //    var slash = name.LastIndexOfAny(new[] { '\\', '/' });
    //    if (slash >= 0 && slash < name.Length - 1)
    //        name = name[(slash + 1)..];

    //    var at = name.IndexOf('@');
    //    if (at > 0) name = name[..at];

    //    return name.ToLowerInvariant();
    //}

    /// <summary>The normalized Windows username, used for every actionby column.</summary>
    public string Username =>
        NormalizeUsername(_http.HttpContext?.User?.Identity?.Name);

    /// <summary>
    /// The APS_Users row for the caller, or null when the caller is not
    /// provisioned or has been deactivated. Cached per request.
    /// </summary>
    public DataRow CurrentUser
    {
        get
        {
            var context = _http.HttpContext;
            if (context == null) return null;

            if (context.Items.TryGetValue(ItemsKey, out var cached))
                return cached as DataRow;

            DataRow row = null;
            var username = Username;

            if (!string.IsNullOrEmpty(username))
            {
                row = _db.QuerySingle("APS_UserMaster_GetByUserName",
                    p => ApsDb.In(p, "@UserName", username));
            }

            context.Items[ItemsKey] = row;
            return row;
        }
    }

    public bool IsProvisioned => CurrentUser != null;

    /// <summary>Access type of the caller, or 0 when not provisioned.</summary>
    public byte AccessType
    {
        get
        {
            var row = CurrentUser;
            return row == null ? (byte)0 : Convert.ToByte(row["RoleID"]);
        }
    }

    public string RoleName
    {
        get
        {
            EnsureRoleNames();
            return RoleNameFor(AccessType);
        }
    }

    /// <summary>
    /// Display name for a role id. APS_RoleMaster is the authority; the built-in
    /// names below are only a fallback for the first request after a restart, or
    /// if the table cannot be read. Every existing call site keeps working.
    /// </summary>
    public static string RoleNameFor(int accessType)
    {
        var cached = _roleNames;
        if (cached != null && cached.TryGetValue(accessType, out var name)
            && !string.IsNullOrWhiteSpace(name))
            return name;

        return accessType switch
        {
            RoleAdmin => "Admin",
            RoleManager => "Manager",
            RoleAssociate => "Reviewer",
            _ => "Unassigned"
        };
    }

    // -------------------------------------------------------------------------
    // Role names from APS_RoleMaster.
    //
    // Cached in memory because the top bar renders the role on every request and
    // the table changes perhaps once a year. The constants above stay as the
    // identifiers used by [ApsAuthorize] - only what the user reads comes from
    // the database, so authorization is unaffected by a renamed row.
    // -------------------------------------------------------------------------
    private static readonly object RoleLock = new();
    private static readonly TimeSpan RoleCacheWindow = TimeSpan.FromMinutes(10);
    private static Dictionary<int, string> _roleNames;
    private static DateTime _rolesLoadedUtc;

    private void EnsureRoleNames()
    {
        lock (RoleLock)
        {
            if (_roleNames != null && DateTime.UtcNow - _rolesLoadedUtc < RoleCacheWindow)
                return;

            try
            {
                var table = _db.Query("APS_RoleMaster_Get");
                var names = new Dictionary<int, string>();

                foreach (DataRow row in table.Rows)
                    names[Convert.ToInt32(row["RoleID"])] = Convert.ToString(row["RoleName"]);

                _roleNames = names;
                _rolesLoadedUtc = DateTime.UtcNow;
            }
            catch
            {
                // A database hiccup must not blank the top bar: the previous
                // snapshot, or the built-in names, carry on.
                _roleNames ??= new Dictionary<int, string>();
                _rolesLoadedUtc = DateTime.UtcNow;
            }
        }
    }
    public string DisplayName
    {
        get
        {
            var row = CurrentUser;
            if (row == null) return Username;
            return $"{row["FirstName"]} {row["LastName"]}".Trim();
        }
    }

    /// <summary>Initials for the top bar avatar.</summary>
    public string Initials
    {
        get
        {
            var row = CurrentUser;
            if (row == null)
                return (Username.Length > 0 ? Username[..Math.Min(2, Username.Length)] : "??").ToUpperInvariant();

            var first = Convert.ToString(row["FirstName"]);
            var last = Convert.ToString(row["LastName"]);
            var initials = string.Concat(
                first.Length > 0 ? first[0].ToString() : "",
                last.Length > 0 ? last[0].ToString() : "");
            return initials.Length > 0 ? initials.ToUpperInvariant() : "??";
        }
    }

    public bool IsInRole(params byte[] accessTypes) =>
        accessTypes != null && Array.IndexOf(accessTypes, AccessType) >= 0;
}
