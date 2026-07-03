using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using SiaWebhookApi.Models;
using SiaWebhookApi.Services;
using SiaWebhookApi.Configuration;
using SiaWebhookApi.Filters;

var builder = WebApplication.CreateBuilder(args);

// Validate SiaSettings section exists at startup
if (builder.Configuration.GetSection("SiaSettings").Get<SiaSettings>() == null)
    throw new InvalidOperationException("SiaSettings missing from appsettings.json");

// Resolve auth config (secrets from env vars, with insecure dev fallbacks + warnings).
// A throwaway logger is used because the DI container isn't built yet.
AuthOptions authOptions;
using (var bootstrapLoggerFactory = LoggerFactory.Create(b => b.AddConsole()))
{
    authOptions = AuthOptions.Load(builder.Configuration, bootstrapLoggerFactory.CreateLogger("Startup"), builder.Environment.IsProduction());
}
builder.Services.AddSingleton(authOptions);
builder.Services.AddSingleton<AuthService>();

// JWT bearer authentication for the settings API.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = authOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.JwtSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

// Per-IP rate limiting for the public webhook: 10 requests/minute.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("zepcam-webhook", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1)
            }));

    // Throttle login to slow brute-force password guessing: 5 attempts/minute per IP.
    options.AddPolicy("auth-login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1)
            }));
});

// Honour X-Forwarded-For/Proto from the reverse proxy (Coolify/Traefik) so the
// client IP used for rate limiting and logging is the real caller, not the proxy.
// SECURITY: clearing known networks/proxies trusts the forwarded header from any
// source. Safe only because the container port is reachable solely by the proxy.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// Core services
builder.Services.AddSingleton<ISiaTransmitter, SiaTransmitter>();
builder.Services.AddSingleton<SiaSequenceCounter>();
builder.Services.AddSingleton<SiaSettingsService>();
builder.Services.AddSingleton<ISiaAlarmService, SiaAlarmService>();

var app = builder.Build();

// Pure JSON API now — the UI is the separate Next.js app. No static files or
// antiforgery needed: auth is JWT bearer (settings) and a static key (webhook),
// neither of which is vulnerable to CSRF.
app.UseForwardedHeaders();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Liveness probe for the orchestrator (Coolify/Docker healthcheck). Anonymous.
app.MapGet("/health", () => Results.Ok("OK"));

// Login endpoint — exchanges the admin password for a short-lived JWT.
app.MapPost("/api/auth/login", (LoginRequest request, AuthService auth) =>
{
    if (!auth.VerifyPassword(request.Password))
        return Results.Unauthorized();

    var (token, expiresUtc) = auth.GenerateToken();
    return Results.Ok(new { token, expiresUtc });
})
    .RequireRateLimiting("auth-login");

// Settings API endpoints — protected by JWT bearer auth.
app.MapGet("/api/settings", (SiaSettingsService svc) =>
    Results.Ok(svc.Get()))
    .RequireAuthorization();

app.MapPut("/api/settings", async (SiaSettings updated, SiaSettingsService svc) =>
{
    var (success, error) = await svc.UpdateAsync(updated);
    return success ? Results.Ok() : Results.Problem(error);
})
    .RequireAuthorization();

// Connectivity check — attempts a TCP connection to the configured SIA receiver.
app.MapPost("/api/settings/test-connection", async (SiaSettingsService svc) =>
{
    var (success, error) = await svc.TestConnectionAsync();
    return success
        ? Results.Ok(new { success = true })
        : Results.Ok(new { success = false, error });
})
    .RequireAuthorization();

// Webhook endpoint — receives Zepcam panic events and forwards them as SIA DC-09 alarms
app.MapPost("/zepcam-panic", async (
    DataTransferObject data,
    SiaSettingsService settingsSvc,
    ISiaAlarmService alarmSvc) =>
{
    if (data.Location is null || string.IsNullOrEmpty(data.Location.Latitude) || string.IsNullOrEmpty(data.Location.Longitude))
        return Results.BadRequest("Missing or incomplete 'location' field (latitude/longitude required).");
    if (string.IsNullOrEmpty(data.UserName))
        return Results.BadRequest("Missing 'user_name' field.");
    if (string.IsNullOrEmpty(data.UserExternalId))
        return Results.BadRequest("Missing 'user_externalid' field.");

    await alarmSvc.SendAlarmAsync(data, settingsSvc.Get());
    return Results.Ok(new { Status = "Processed" });
})
    .AddEndpointFilter<ZepcamApiKeyFilter>()
    .RequireRateLimiting("zepcam-webhook")
    // Legit Zepcam payloads are well under 1 KB; 8 KB gives headroom while
    // making body-size DoS impossible (Kestrel default is 30 MB).
    .WithMetadata(new RequestSizeLimitAttribute(8192));

app.Run();

// Exposes the implicit Program class to WebApplicationFactory-based integration tests.
public partial class Program;
