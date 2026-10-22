using System.Data;
using AgenticPitstop.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace AgenticPitstop.Controllers;

/// <summary>
/// Agents Scheduler (Admin/Manager) and Agents Monitor (everyone).
/// </summary>
[ApsAuthorize]
public sealed class AgentsController : ApsController
{
    public AgentsController(ApsDb db, ApsIdentity identity, ApsMetadata meta)
        : base(db, identity, meta) { }

    protected override string ActiveScreenKey =>
        string.Equals(ControllerContext.ActionDescriptor.ActionName, "Monitor",
                      StringComparison.OrdinalIgnoreCase)
            ? "agent-monitor"
            : "agent-scheduler";

    [HttpGet]
    [ApsAuthorize(ApsIdentity.RoleAdmin, ApsIdentity.RoleManager)]
    public IActionResult Scheduler()
    {
        ViewBag.Agents = Db.Query("APS_Agent_Get");
        return View();
    }

    [HttpGet]
    public IActionResult Monitor()
    {
        ViewBag.Agents = Db.Query("APS_Agent_Get");
        ViewBag.Executions = Db.Query("APS_AgentExecution_Get", p =>
        {
            ApsDb.In(p, "@agid", null);
            ApsDb.In(p, "@top", 25);
        });
        return View();
    }

    [HttpGet]
    [ApsAuthorize(ApsIdentity.RoleAdmin, ApsIdentity.RoleManager)]
    public IActionResult Get(short id)
    {
        var row = Db.QuerySingle("APS_Agent_GetById", p => ApsDb.In(p, "@agid", id));
        return row == null ? Fail("Agent not found.") : Ok(null, ApsMetadata.ToDictionary(row));
    }

    [HttpGet]
    public IActionResult Executions(short id)
    {
        var table = Db.Query("APS_AgentExecution_Get", p =>
        {
            ApsDb.In(p, "@agid", id);
            ApsDb.In(p, "@top", 20);
        });
        return Ok(null, ApsMetadata.ToList(table));
    }

    [HttpPost]
    [ApsAuthorize(ApsIdentity.RoleAdmin, ApsIdentity.RoleManager)]
    public IActionResult Save(short agid, string agentname, string purpose, string apiendpoint,
                              string apiparams, byte scheduletype, string scheduletime,
                              byte? everyminutes, byte activestatus)
    {
        if (string.IsNullOrWhiteSpace(agentname)) return Fail("Agent name is required.");
        if (string.IsNullOrWhiteSpace(purpose)) return Fail("Purpose is required.");

        if (!IsHttpUrl(apiendpoint))
            return Fail("Enter an API endpoint starting with http:// or https://.");

        if (scheduletype == 2)
        {
            if (everyminutes is null or 0 or > 99)
                return Fail("Frequency must be between 1 and 99 minutes.");
        }
        else if (string.IsNullOrWhiteSpace(scheduletime))
        {
            return Fail("Select a daily run time.");
        }

        if (!string.IsNullOrWhiteSpace(apiparams) && !LooksLikeJson(apiparams))
            return Fail("API parameters must be a JSON object.");

        var outputs = Db.ExecuteWithOutput("APS_Agent_Save", p =>
        {
            ApsDb.In(p, "@agid", agid);
            ApsDb.In(p, "@agentname", agentname.Trim());
            ApsDb.In(p, "@purpose", purpose.Trim());
            ApsDb.In(p, "@apiendpoint", apiendpoint.Trim());
            ApsDb.In(p, "@apiparams", string.IsNullOrWhiteSpace(apiparams) ? null : apiparams.Trim());
            ApsDb.In(p, "@scheduletype", scheduletype);
            ApsDb.In(p, "@scheduletime", scheduletype == 1 ? scheduletime : null);
            ApsDb.In(p, "@everyminutes", scheduletype == 2 ? everyminutes : null);
            ApsDb.In(p, "@activestatus", activestatus);
            ApsDb.In(p, "@actionby", ActionBy);
            ApsDb.Out(p, "@newagid", SqlDbType.SmallInt);
            ApsDb.Out(p, "@message", SqlDbType.NVarChar, 200);
        });

        return FromProcedure(outputs, "newagid");
    }

    [HttpPost]
    [ApsAuthorize(ApsIdentity.RoleAdmin, ApsIdentity.RoleManager)]
    public IActionResult Delete(short agid)
    {
        Db.Execute("APS_Agent_Delete", p =>
        {
            ApsDb.In(p, "@agid", agid);
            ApsDb.In(p, "@actionby", ActionBy);
        });
        return Ok("Agent deactivated.");
    }

    internal static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    internal static bool LooksLikeJson(string value)
    {
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(value);
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
