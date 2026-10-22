using System.Data;
using System.Text.RegularExpressions;
using AgenticPitstop.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace AgenticPitstop.Controllers;

/// <summary>
/// User Management. Admin and Manager only. Deletes are soft: the row stays,
/// is marked deleted and inactive, and every change is written to APS_UserAudit.
/// </summary>
[ApsAuthorize(ApsIdentity.RoleAdmin, ApsIdentity.RoleManager)]
public sealed class UsersController : ApsController
{
    public UsersController(ApsDb db, ApsIdentity identity, ApsMetadata meta)
        : base(db, identity, meta) { }

    protected override string ActiveScreenKey => "user-management";

    [HttpGet]
    public IActionResult Index(string search = null, string status = null)
    {
        var filter = ParseStatus(status);

        ViewBag.Users = Db.Query("APS_Users_Get", p =>
        {
            ApsDb.In(p, "@Search", string.IsNullOrWhiteSpace(search) ? null : search.Trim());
            ApsDb.In(p, "@IsActive", filter);
        });

        ViewBag.History = Db.Query("APS_Users_History_Get", p => ApsDb.In(p, "@top", 12));
        ViewBag.Roles = Db.Query("APS_RoleMaster_Get");
        ViewBag.Search = search;
     
        ViewBag.Status = status ?? "All statuses";

        return View();
    }

    [HttpGet]
    public IActionResult Get(int id)
    {
        var row = Db.QuerySingle("APS_Users_GetById", p => ApsDb.In(p, "@uid", id));
        return row == null
            ? Fail("User not found.")
            : Ok(null, ApsMetadata.ToDictionary(row));
    }

    [HttpPost]
    public IActionResult Save(int uid, string portalid, string firstname, string lastname,
                              string emailid, byte accesstype)
    {
        // Server-side validation. The browser checks the same rules for a faster
        // response, but this is the copy that decides.
        var problem = ValidateUser(portalid, firstname, lastname, emailid, accesstype);
        if (problem != null) return Fail(problem);

        // A Manager must not be able to mint another Admin, or change one.
        if (accesstype == ApsIdentity.RoleAdmin && Identity.AccessType != ApsIdentity.RoleAdmin)
            return Fail("Only an Admin can assign the Admin role.");

        if (uid > 0 && Identity.AccessType != ApsIdentity.RoleAdmin && IsAdminAccount(uid))
            return Fail("Only an Admin can change an Admin account.");

        portalid = ApsIdentity.NormalizeUsername(portalid);

        if (uid == 0)
        {
            var outputs = Db.ExecuteWithOutput("APS_Users_Insert", p =>
            {
                ApsDb.In(p, "@portalid", portalid);
                ApsDb.In(p, "@firstname", firstname.Trim());
                ApsDb.In(p, "@lastname", lastname.Trim());
                ApsDb.In(p, "@emailid", emailid.Trim());
                ApsDb.In(p, "@accesstype", (int)accesstype);
                ApsDb.In(p, "@actionby", ActionBy);
                ApsDb.Out(p, "@uid", SqlDbType.Int);
                ApsDb.Out(p, "@message", SqlDbType.VarChar, 200);
            });
            return FromProcedure(outputs, "uid");
        }

        var updateOutputs = Db.ExecuteWithOutput("APS_Users_Update", p =>
        {
            ApsDb.In(p, "@uid", uid);
            ApsDb.In(p, "@portalid", portalid);
            ApsDb.In(p, "@firstname", firstname.Trim());
            ApsDb.In(p, "@lastname", lastname.Trim());
            ApsDb.In(p, "@emailid", emailid.Trim());
            ApsDb.In(p, "@accesstype", (int)accesstype);
            ApsDb.In(p, "@actionby", ActionBy);
            ApsDb.Out(p, "@success", SqlDbType.TinyInt);
            ApsDb.Out(p, "@message", SqlDbType.VarChar, 200);
        });

        return FromProcedure(updateOutputs);
    }

    [HttpPost]
    public IActionResult ToggleStatus(int uid)
    {
        if (IsSelf(uid)) return Fail("You cannot change your own active status.");

        if (Identity.AccessType != ApsIdentity.RoleAdmin && IsAdminAccount(uid))
            return Fail("Only an Admin can change an Admin account.");

        var outputs = Db.ExecuteWithOutput("APS_Users_ToggleStatus", p =>
        {
            ApsDb.In(p, "@uid", uid);
            ApsDb.In(p, "@actionby", ActionBy);
            ApsDb.Out(p, "@success", SqlDbType.TinyInt);
            ApsDb.Out(p, "@message", SqlDbType.VarChar, 200);
        });

        return FromProcedure(outputs);
    }

    [HttpPost]
    public IActionResult Delete(int uid)
    {
        // Removing your own access would lock you out of the screen you are on.
        if (IsSelf(uid)) return Fail("You cannot delete your own account.");

        if (Identity.AccessType != ApsIdentity.RoleAdmin && IsAdminAccount(uid))
            return Fail("Only an Admin can delete an Admin account.");

        var outputs = Db.ExecuteWithOutput("APS_Users_SoftDelete", p =>
        {
            ApsDb.In(p, "@uid", uid);
            ApsDb.In(p, "@actionby", ActionBy);
            ApsDb.Out(p, "@success", SqlDbType.TinyInt);
            ApsDb.Out(p, "@message", SqlDbType.VarChar, 200);
        });

        return FromProcedure(outputs);
    }

    // -------------------------------------------------------------------------

    private bool IsAdminAccount(int uid)
    {
        var row = Db.QuerySingle("APS_Users_GetById", p => ApsDb.In(p, "@uid", uid));
        return row != null && Convert.ToInt32(row["accesstype"]) == ApsIdentity.RoleAdmin;
    }

    private bool IsSelf(int uid)
    {
        var current = Identity.CurrentUser;
        if (current == null) return false;

        var idColumn = current.Table.Columns.Cast<DataColumn>()
            .FirstOrDefault(c => string.Equals(c.ColumnName, "UserID", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(c.ColumnName, "uid", StringComparison.OrdinalIgnoreCase));

        return idColumn != null && Convert.ToInt32(current[idColumn]) == uid;
    }

    private static byte? ParseStatus(string status) => status switch
    {
        "Active" => (byte)1,
        "Inactive" => (byte)0,
        _ => null
    };

    // Compiled once: both run on every save.
    private static readonly Regex LettersOnly =
        new(@"^[A-Za-z]+$", RegexOptions.Compiled);

    // Letters, digits, dot and the @ - nothing else.
    private static readonly Regex EmailShape =
        new(@"^[A-Za-z0-9.]+@[A-Za-z0-9.]+\.[A-Za-z]{2,}$", RegexOptions.Compiled);

    private static string ValidateUser(string portalid, string firstname, string lastname,
                                       string emailid, byte accesstype)
    {
        if (string.IsNullOrWhiteSpace(portalid)) return "Portal ID is required.";

        // Normalized first, so DOMAIN\356116 and 356116.adm are judged by the
        // same value the sign-in resolves to.
        var normalizedPortalId = ApsIdentity.NormalizeUsername(portalid);
        if ((normalizedPortalId.Length != 6 && normalizedPortalId.Length != 8)
            || !normalizedPortalId.All(char.IsDigit))
            return "Portal ID must be 6 or 8 digits.";

        if (string.IsNullOrWhiteSpace(firstname)) return "First name is required.";
        if (!LettersOnly.IsMatch(firstname.Trim()))
            return "First name must contain letters only.";

        if (string.IsNullOrWhiteSpace(lastname)) return "Last name is required.";
        if (!LettersOnly.IsMatch(lastname.Trim()))
            return "Last name must contain letters only.";

        if (string.IsNullOrWhiteSpace(emailid)) return "Email address is required.";
        if (!EmailShape.IsMatch(emailid.Trim()))
            return "Enter a valid email address. Letters, numbers, dot and @ only.";

        if (accesstype == 0)
            return "Select a valid role.";

        return null;
    }
}