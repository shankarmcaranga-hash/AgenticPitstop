using AgenticPitstop.Infrastructure;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Server.IISIntegration;
using AgenticPitstop.Infrastructure;
        
var builder = WebApplication.CreateBuilder(args);
ApsSecrets.Apply(builder.Configuration);
// -----------------------------------------------------------------------------
// Authentication: Windows today, SSO later.
// Negotiate covers Kestrel and HTTP.sys; the IIS handler covers IIS / IIS Express.
// Swapping to SSO means replacing these two registrations — ApsIdentity, the RBAC
// filter and every controller keep working, because they read the username from
// HttpContext.User rather than from a specific handler.
// -----------------------------------------------------------------------------
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
                .AddNegotiate();

builder.Services.AddAuthorization(options =>
{
    // Anonymous requests never reach a controller.
    options.FallbackPolicy = options.DefaultPolicy;
});

builder.Services.Configure<IISServerOptions>(options =>
{
    options.AutomaticAuthentication = true;
});

// -----------------------------------------------------------------------------
// Application services
// -----------------------------------------------------------------------------
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ApsDb>();
builder.Services.AddScoped<ApsIdentity>();
builder.Services.AddScoped<ApsMetadata>();
builder.Services.AddScoped<ApsDetailRuntime>();
builder.Services.AddScoped<ApsFileRoots>();
builder.Services.AddApiConfigurator();
builder.Services.AddInflowArchive();
builder.Services.AddSopAnalyzer();

// The connectivity/API test on the Configuration screen calls the customer's
// endpoint from the server, never from the browser, so credentials in the
// configured headers are not exposed to the client.
builder.Services.AddHttpClient("ApsInflowProbe", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});

builder.Services.AddControllersWithViews(options =>
{
    // Anti-forgery on every non-GET request, including the AJAX save endpoints.
    options.Filters.Add(
        new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
    options.Cookie.Name = "APS.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

var app = builder.Build();

// A path base lets one IIS site serve /customerabc, /customerxyz and so on as
// separate applications, each bound to its own database. It has to be registered
// before routing, or generated links will drop the prefix.
var pathBase = builder.Configuration["Aps:PathBase"];
if (!string.IsNullOrWhiteSpace(pathBase))
{
    app.UsePathBase(pathBase);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

// -----------------------------------------------------------------------------
// Security headers. Inline script is allowed only when it carries this request's
// nonce. Note that a browser ignores 'unsafe-inline' whenever a nonce is present,
// so the two must never be combined.
// -----------------------------------------------------------------------------
var isDevelopment = app.Environment.IsDevelopment();

app.Use(async (context, next) =>
{
    var nonce = context.ApsNonce();
    var headers = context.Response.Headers;

    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "SAMEORIGIN";
    headers["Referrer-Policy"] = "same-origin";

    // Browser Link and dotnet watch open a websocket back to the IDE on a
    // random localhost port. Widened for Development only.
    var connectSrc = isDevelopment
        ? "connect-src 'self' ws: wss: http://localhost:* https://localhost:*"
        : "connect-src 'self'";

    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        $"script-src 'self' 'nonce-{nonce}'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        connectSrc + "; " +
        "frame-ancestors 'self'; " +
        "base-uri 'self'; " +
        "form-action 'self'";

    await next();
});

app.UseHttpsRedirection();
app.UseStaticFiles();
// The secret store failed at startup. Every screen needs the database, so the
// cause is shown rather than letting each one fail with its own SQL error.
app.Use(async (context, next) =>
{
    if (ApsSecrets.StartupError == null) { await next(); return; }

    context.Response.StatusCode = 503;
    context.Response.ContentType = "text/html; charset=utf-8";

    await context.Response.WriteAsync(
        "<!doctype html><html><head><meta charset='utf-8'>" +
        "<title>Agentic PITSTOP - configuration problem</title>" +
        "<link rel='stylesheet' href='" + context.Request.PathBase + "/css/site.css'></head>" +
        "<body><div style='max-width:760px;margin:12vh auto;padding:0 20px'>" +
        "<h1 style='margin:0 0 10px'>The portal cannot start</h1>" +
        "<p style='color:#667085'>Its database connection could not be loaded.</p>" +
        "<div class='callout callout-danger'>" +
        System.Net.WebUtility.HtmlEncode(ApsSecrets.StartupError) +
        "</div><p style='color:#667085;font-size:12px'>" +
        "Fix the cause above and restart the application.</p></div></body></html>");
});
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// After authorization, so only a recognised caller's activity is tracked.
app.UseApsIdleTimeout();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Inbox}/{action=Index}/{id?}");

app.Run();
