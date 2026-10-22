using Microsoft.AspNetCore.DataProtection;

namespace AgenticPitstop.Infrastructure;

/// <summary>
/// Idle timeout for a Windows-authenticated portal.
///
/// Negotiate has no sign-in cookie to expire - the browser re-authenticates
/// silently - so an unattended screen would otherwise stay usable for ever.
/// This tracks last activity in a protected cookie and, once the window has
/// passed, sends the user to a Session expired page. Continuing from there
/// re-authenticates and re-reads APS_UserMaster, so an account disabled in the
/// meantime is stopped at that point.
///
/// Set Aps:IdleTimeoutMinutes to 0 to switch it off entirely.
/// </summary>
public sealed class ApsIdleTimeoutOptions
{
    public int Minutes { get; set; } = 20;
    public string CookieName { get; set; } = "APS.Activity";
}

public sealed class ApsIdleTimeoutMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ApsIdleTimeoutOptions _options;
    private readonly IDataProtector _protector;

    public ApsIdleTimeoutMiddleware(RequestDelegate next,
                                    ApsIdleTimeoutOptions options,
                                    IDataProtectionProvider protection)
    {
        _next = next;
        _options = options;
        _protector = protection.CreateProtector("Aps.IdleTimeout.v1");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!ShouldTrack(context))
        {
            await _next(context);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var lastSeen = ReadLastSeen(context);

        if (lastSeen != null && now - lastSeen.Value > TimeSpan.FromMinutes(_options.Minutes))
        {
            Clear(context);
            await ExpireAsync(context);
            return;
        }

        Stamp(context, now);
        await _next(context);
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Static files and the expiry page itself must not refresh the clock, or a
    /// page left open loading images would never time out.
    /// </summary>
    private static bool ShouldTrack(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (path.StartsWith("/css/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/js/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/lib/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/images/", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            return false;

        if (path.StartsWith("/Home/SessionExpired", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/Home/Denied", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/Home/Error", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private DateTimeOffset? ReadLastSeen(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(_options.CookieName, out var value) ||
            string.IsNullOrEmpty(value))
            return null;

        try
        {
            var ticks = long.Parse(_protector.Unprotect(value));
            return new DateTimeOffset(ticks, TimeSpan.Zero);
        }
        catch
        {
            // Tampered, or protected by a key this server no longer has.
            return null;
        }
    }

    private void Stamp(HttpContext context, DateTimeOffset now)
    {
        context.Response.Cookies.Append(
            _options.CookieName,
            _protector.Protect(now.UtcTicks.ToString()),
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                // Same as request: secure over HTTPS, still works over plain HTTP.
                Secure = context.Request.IsHttps
                // No Expires: closing the browser ends it too.
            });
    }

    private void Clear(HttpContext context) =>
        context.Response.Cookies.Delete(_options.CookieName);

    private static Task ExpireAsync(HttpContext context)
    {
        var isAjax = string.Equals(
            context.Request.Headers["X-Requested-With"],
            "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

        if (isAjax)
        {
            // Same envelope every APS action returns, so callers need no new path.
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync(
                "{\"success\":false,\"message\":\"Your session has expired. Reloading...\",\"expired\":true}");
        }

        // PathBase is honoured, so /customerabc keeps its prefix.
        context.Response.Redirect(context.Request.PathBase + "/Home/SessionExpired");
        return Task.CompletedTask;
    }
}

public static class ApsIdleTimeoutExtensions
{
    public static IApplicationBuilder UseApsIdleTimeout(this WebApplication app)
    {
        var minutes = app.Configuration.GetValue<int?>("Aps:IdleTimeoutMinutes") ?? 20;
        if (minutes <= 0) return app;          // 0 disables the feature

        return app.UseMiddleware<ApsIdleTimeoutMiddleware>(
            new ApsIdleTimeoutOptions { Minutes = minutes });
    }
}