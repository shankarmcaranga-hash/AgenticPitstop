using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AgenticPitstop.Infrastructure;

/// <summary>
/// Server-side RBAC. Hiding a link in the sidebar is a courtesy, not a boundary —
/// this attribute is the boundary, and every action that touches configuration or
/// user administration carries it.
///
/// Usage: [ApsAuthorize(ApsIdentity.RoleAdmin, ApsIdentity.RoleManager)]
/// With no arguments it means "any provisioned, active user".
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class ApsAuthorizeAttribute : Attribute, IAuthorizationFilter
{
    private readonly byte[] _allowed;

    public ApsAuthorizeAttribute(params byte[] allowedAccessTypes)
    {
        _allowed = allowedAccessTypes ?? Array.Empty<byte>();
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var identity = context.HttpContext.RequestServices.GetRequiredService<ApsIdentity>();

        // Windows authenticated the caller, but the portal still has to recognise them.
        if (!identity.IsProvisioned)
        {
            context.Result = Deny(context, "NotProvisioned");
            return;
        }

        if (_allowed.Length > 0 && !identity.IsInRole(_allowed))
        {
            context.Result = Deny(context, "Forbidden");
        }
    }

    private static IActionResult Deny(AuthorizationFilterContext context, string reason)
    {
        var isAjax = string.Equals(
            context.HttpContext.Request.Headers["X-Requested-With"],
            "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

        if (isAjax)
        {
            return new JsonResult(new { success = false, message = "You do not have access to this action." })
            {
                StatusCode = reason == "NotProvisioned" ? 401 : 403
            };
        }

        return new RedirectToActionResult("Denied", "Home", new { reason });
    }
}
