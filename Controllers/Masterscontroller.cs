using AgenticPitstop.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace AgenticPitstop.Controllers;

/// <summary>
/// The administration masters: Client Master, Use Case Master, and which use
/// cases a user may open.
///
/// Three screens share one controller because they are three faces of the same
/// data - a use case belongs to a client, and a mapping grants a user one use
/// case. Each action carries its own [ApsAuthorize], so the two Admin-only
/// screens and the Admin/Manager mapping screen are gated independently.
///
/// Sidebar visibility is configured in ApsMetadata.Screens. That is a courtesy;
/// the attributes below are the boundary.
/// </summary>
public sealed class MastersController : ApsController
{
    public MastersController(ApsDb db, ApsIdentity identity, ApsMetadata meta)
        : base(db, identity, meta) { }

    /// <summary>
    /// ApsController reads this in OnActionExecuting, which runs before the
    /// action body, so it is derived from the action name rather than set inside
    /// an action. Same approach as AgentsController.
    /// </summary>
    protected override string ActiveScreenKey =>
        ControllerContext.ActionDescriptor.ActionName switch
        {
            "ClientMaster" => "client-master",
            "UseCaseMaster" => "usecase-master",
            _ => "usecase-mapping"
        };

    // =========================================================================
    // Client Master  (Admin)
    // =========================================================================

    [HttpGet]
    [ApsAuthorize(ApsIdentity.RoleAdmin)]
    public IActionResult ClientMaster()
    {
        ViewBag.Title = "Client Master";

        // @ActiveOnly = 0, because a deactivated client has to be visible here
        // to be reactivated. Every other caller takes the default of 1.
        ViewBag.Clients = Db.Query("APS_ClientMaster_Get",
            p => ApsDb.In(p, "@ActiveOnly", (byte)0));

        return View();
    }

    // Letters and underscore only. The code is concatenated into UseCaseCode,
    // and the name is shown wherever a use case is named.
    private static readonly System.Text.RegularExpressions.Regex ClientText =
        new(@"^[A-Za-z_]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    [HttpPost]
    [ApsAuthorize(ApsIdentity.RoleAdmin)]
    public IActionResult SaveClient(int clientId, string clientCode, string clientName)
    {
        clientCode = (clientCode ?? string.Empty).Trim();
        clientName = (clientName ?? string.Empty).Trim();

        if (clientCode.Length == 0 || clientCode.Length > 20)
            return Fail("Enter a client code of up to 20 characters.");

        if (!ClientText.IsMatch(clientCode))
            return Fail("Use letters and underscore only in the client code.");

        if (clientName.Length == 0 || clientName.Length > 200)
            return Fail("Enter a client name of up to 200 characters.");

        if (!ClientText.IsMatch(clientName))
            return Fail("Use letters and underscore only in the client name.");

        var outputs = Db.ExecuteWithOutput("APS_ClientMaster_Save", p =>
        {
            ApsDb.In(p, "@ClientID", clientId);
            ApsDb.In(p, "@ClientCode", clientCode);
            ApsDb.In(p, "@ClientName", clientName);
            ApsDb.In(p, "@ActionBy", ActionBy);
            ApsDb.Out(p, "@NewClientID", SqlDbType.Int);
            ApsDb.Out(p, "@Message", SqlDbType.VarChar, 200);
        });

        return FromProcedure(outputs, "NewClientID");
    }

    [HttpPost]
    [ApsAuthorize(ApsIdentity.RoleAdmin)]
    public IActionResult ToggleClient(int clientId)
    {
        if (clientId <= 0) return Fail("Select a client first.");

        var outputs = Db.ExecuteWithOutput("APS_ClientMaster_ToggleStatus", p =>
        {
            ApsDb.In(p, "@ClientID", clientId);
            ApsDb.In(p, "@ActionBy", ActionBy);
            ApsDb.Out(p, "@Success", SqlDbType.TinyInt);
            ApsDb.Out(p, "@Message", SqlDbType.VarChar, 200);
        });

        return FromProcedure(outputs);
    }

    // =========================================================================
    // Use Case Master  (Admin)
    // =========================================================================

    [HttpGet]
    [ApsAuthorize(ApsIdentity.RoleAdmin)]
    public IActionResult UseCaseMaster()
    {
        ViewBag.Title = "Use Case Master";

        ViewBag.UseCases = Db.Query("APS_UseCaseMaster_GetAll");

        // Only active clients and processes can be combined into a new use case.
        ViewBag.Clients = Db.Query("APS_ClientMaster_Get");
        ViewBag.Processes = Db.Query("APS_ProcessMaster_Get");

        return View();
    }

    /// <summary>
    /// The existing APS_UseCaseMaster_Save derives UseCaseCode and UseCaseName
    /// from the client and process, so the screen sends only those two ids.
    /// Nothing about that procedure is changed.
    /// </summary>
    [HttpPost]
    [ApsAuthorize(ApsIdentity.RoleAdmin)]
    public IActionResult SaveUseCase(int useCaseId, int clientId, int processId)
    {
        // A use case is created, never re-pointed. Changing its client or process
        // would silently relabel every template, collection, pipeline, run log and
        // archived file already attached to this UseCaseID.
        if (useCaseId != 0)
            return Fail("A use case cannot be changed. Deactivate it and create a new one.");

        if (clientId <= 0) return Fail("Choose a client.");
        if (processId <= 0) return Fail("Choose a process.");

        var outputs = Db.ExecuteWithOutput("APS_UseCaseMaster_Save", p =>
        {
            ApsDb.In(p, "@UseCaseID", useCaseId);
            ApsDb.In(p, "@ClientID", clientId);
            ApsDb.In(p, "@ProcessID", processId);
            ApsDb.In(p, "@ActionBy", ActionBy);
            ApsDb.Out(p, "@NewUseCaseID", SqlDbType.Int);
            ApsDb.Out(p, "@Message", SqlDbType.VarChar, 200);
        });

        return FromProcedure(outputs, "NewUseCaseID");
    }

    [HttpPost]
    [ApsAuthorize(ApsIdentity.RoleAdmin)]
    public IActionResult ToggleUseCase(int useCaseId)
    {
        if (useCaseId <= 0) return Fail("Select a use case first.");

        var outputs = Db.ExecuteWithOutput("APS_UseCaseMaster_ToggleStatus", p =>
        {
            ApsDb.In(p, "@UseCaseID", useCaseId);
            ApsDb.In(p, "@ActionBy", ActionBy);
            ApsDb.Out(p, "@Success", SqlDbType.TinyInt);
            ApsDb.Out(p, "@Message", SqlDbType.VarChar, 200);
        });

        return FromProcedure(outputs);
    }

    // =========================================================================
    // Use Case Mapping  (Admin and Manager)
    // =========================================================================

    [HttpGet]
    [ApsAuthorize(ApsIdentity.RoleAdmin, ApsIdentity.RoleManager)]
    public IActionResult UseCaseMapping(int userId = 0)
    {
        ViewBag.Title = "Use Case Mapping";
        ViewBag.Users = Db.Query("APS_UserMaster_GetForMapping");
        ViewBag.SelectedUserId = userId;
        return View();
    }

    /// <summary>
    /// Returns one user's use case list, so changing the user in the picker does
    /// not reload the whole screen.
    /// </summary>
    [HttpGet]
    [ApsAuthorize(ApsIdentity.RoleAdmin, ApsIdentity.RoleManager)]
    public IActionResult MappingFor(int userId)
    {
        if (userId <= 0) return Fail("Select a user first.");

        var rows = Db.Query("APS_UserUseCaseMapping_Get",
            p => ApsDb.In(p, "@UserID", userId));

        var data = new List<object>();

        foreach (DataRow row in rows.Rows)
        {
            data.Add(new
            {
                useCaseId = Convert.ToInt32(row["UseCaseID"]),
                useCaseCode = ApsMetadata.Cell(row, "UseCaseCode"),
                useCaseName = ApsMetadata.Cell(row, "UseCaseName"),
                clientName = ApsMetadata.Cell(row, "ClientName"),
                processName = ApsMetadata.Cell(row, "ProcessName"),
                isMapped = Convert.ToInt32(row["IsMapped"]) == 1
            });
        }

        return Ok(string.Empty, data);
    }

    [HttpPost]
    [ApsAuthorize(ApsIdentity.RoleAdmin, ApsIdentity.RoleManager)]
    public IActionResult SaveMapping(int userId, string useCaseIds)
    {
        if (userId <= 0) return Fail("Select a user first.");

        // Only well formed ids reach SQL, and the procedure checks each one
        // against the use case master before granting it.
        var ids = (useCaseIds ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => int.TryParse(x.Trim(), out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var outputs = Db.ExecuteWithOutput("APS_UserUseCaseMapping_Save", p =>
        {
            ApsDb.In(p, "@UserID", userId);
            ApsDb.In(p, "@UseCaseIds", string.Join(",", ids));
            ApsDb.In(p, "@ActionBy", ActionBy);
            ApsDb.Out(p, "@Success", SqlDbType.TinyInt);
            ApsDb.Out(p, "@Message", SqlDbType.VarChar, 200);
        });

        return FromProcedure(outputs);
    }
}