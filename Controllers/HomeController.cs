using AgenticPitstop.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace AgenticPitstop.Controllers;

/// <summary>Access denial and error pages. No [ApsAuthorize] — these have to be
/// reachable by a caller the portal does not yet recognise.</summary>
public sealed class HomeController : ApsController
{
    public HomeController(ApsDb db, ApsIdentity identity, ApsMetadata meta)
        : base(db, identity, meta) { }

    [HttpGet]
    public IActionResult Denied(string reason)
    {
        ViewBag.Reason = reason;
        ViewBag.WindowsUser = Identity.Username;
        Response.StatusCode = reason == "NotProvisioned" ? 401 : 403;
        return View();
    }
    [HttpGet]
    public IActionResult SessionExpired()
    {
        ViewBag.Title = "Session expired";
        return View();
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Error()
    {
        ViewBag.RequestId = HttpContext.TraceIdentifier;
        return View();
    }
}
