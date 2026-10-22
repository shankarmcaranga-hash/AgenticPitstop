using AgenticPitstop.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AgenticPitstop.Controllers;

/// <summary>
/// Shared plumbing for every portal screen: service access, the identity and
/// navigation data the layout needs, and one JSON envelope shape so the client
/// script can handle every save the same way.
/// </summary>
public abstract class ApsController : Controller
{
    protected ApsDb Db { get; }
    protected ApsIdentity Identity { get; }
    protected ApsMetadata Meta { get; }

    protected ApsController(ApsDb db, ApsIdentity identity, ApsMetadata meta)
    {
        Db = db;
        Identity = identity;
        Meta = meta;
    }

    /// <summary>The username written to every actionby column, domain removed.</summary>
    protected string ActionBy => Identity.Username;

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        ViewBag.CurrentUserName = Identity.DisplayName;
        ViewBag.CurrentUserInitials = Identity.Initials;
        ViewBag.CurrentRoleName = Identity.RoleName;
        ViewBag.CurrentAccessType = Identity.AccessType;
        ViewBag.NavScreens = ApsMetadata.ScreensFor(Identity.AccessType).ToList();
        ViewBag.ActiveScreen = ActiveScreenKey;

        base.OnActionExecuting(context);
    }

    /// <summary>Screen key used to mark the active sidebar item.</summary>
    protected virtual string ActiveScreenKey => string.Empty;

    // -------------------------------------------------------------------------
    // JSON envelope
    // -------------------------------------------------------------------------

    protected IActionResult Ok(string message, object data = null) =>
        Json(new { success = true, message, data });

    protected IActionResult Fail(string message, object data = null) =>
        Json(new { success = false, message, data });

    /// <summary>
    /// Translates a procedure's OUTPUT parameters into the JSON envelope.
    ///
    /// Every APS_ save procedure reports its outcome explicitly: either a new
    /// identity value that is zero on failure, or a @success bit. Nothing here
    /// inspects the message text, so a reworded validation message can never
    /// silently flip a failure into a success.
    /// </summary>
    protected IActionResult FromProcedure(IDictionary<string, object> outputs,
                                          string idKey = null,
                                          object data = null)
    {
        var message = Convert.ToString(outputs.TryGetValue("message", out var m) ? m : null);

        bool succeeded;
        if (idKey != null && outputs.TryGetValue(idKey, out var idValue))
        {
            var id = idValue == null ? 0 : Convert.ToInt64(idValue);
            succeeded = id > 0;
            data ??= new { id };
        }
        else if (outputs.TryGetValue("success", out var successValue))
        {
            succeeded = successValue != null && Convert.ToBoolean(successValue);
        }
        else
        {
            succeeded = true;
        }

        if (succeeded)
            return Ok(string.IsNullOrWhiteSpace(message) ? "Saved." : message, data);

        return Fail(string.IsNullOrWhiteSpace(message)
            ? "The operation could not be completed."
            : message);
    }
}
