using System.Text;
using System.Net;
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
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

var webhookPermitLimit = GetIntConfig("WEBHOOK_RATE_LIMIT_PER_MINUTE", "WebhookRateLimit:PermitLimit", 60);
var webhookQueueLimit = GetIntConfig("WEBHOOK_RATE_LIMIT_QUEUE", "WebhookRateLimit:QueueLimit", 20);

// Per-IP rate limiting for the public webhook. The default deliberately allows
// a bursty multi-guard incident from one Zepcam NAT IP while still bounding abuse.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("zepcam-webhook", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = webhookPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = webhookQueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
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

// Honour X-Forwarded-For/Proto only from configured reverse proxies. In Coolify,
// set TRUSTED_PROXY_IPS and/or TRUSTED_PROXY_NETWORKS to the Traefik/Docker
// network values instead of trusting forwarded headers from the whole internet.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    ConfigureTrustedForwardedHeaderSources(o);
});

// Core services
var deliveryOptions = SiaDeliveryOptions.Load(builder.Configuration, builder.Environment);
var sequenceOptions = SiaSequenceOptions.Load(builder.Configuration, builder.Environment);
var runtimeSettingsOptions = SiaRuntimeSettingsOptions.Load(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(deliveryOptions);
builder.Services.AddSingleton(sequenceOptions);
builder.Services.AddSingleton(runtimeSettingsOptions);
builder.Services.AddSingleton<ISiaTransmitter, SiaTransmitter>();
builder.Services.AddSingleton<SiaSequenceCounter>();
builder.Services.AddSingleton<SiaSettingsService>();
builder.Services.AddSingleton<IDeadLetterStore, FileDeadLetterStore>();
builder.Services.AddHttpClient<IAlertNotifier, N8nAlertNotifier>();
builder.Services.AddSingleton<ISiaAlarmService, SiaAlarmService>();
builder.Services.AddHostedService<DeadLetterRetryService>();

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
    Results.Ok(svc.GetPublic()))
    .RequireAuthorization();

app.MapPut("/api/settings", async (SiaSettingsUpdateRequest updated, SiaSettingsService svc) =>
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
    ISiaAlarmService alarmSvc,
    CancellationToken cancellationToken) =>
{
    var validation = ZepcamWebhookValidator.Validate(data);
    if (!validation.IsValid)
        return Results.BadRequest(validation.Error);

    var result = await alarmSvc.SendAlarmAsync(data, settingsSvc.GetForTransmission(), cancellationToken);
    return result.Success
        ? Results.Ok(new { Status = "Processed" })
        : Results.Problem(
            title: "SIA alarm delivery failed",
            detail: $"Alarm dead-lettered as {result.DeadLetterId}.",
            statusCode: StatusCodes.Status502BadGateway);
})
    .AddEndpointFilter<ZepcamApiKeyFilter>()
    .RequireRateLimiting("zepcam-webhook")
    .WithMetadata(new RequestSizeLimitAttribute(8192));

app.Run();

int GetIntConfig(string envName, string configKey, int fallback)
{
    var raw = Environment.GetEnvironmentVariable(envName) ?? builder.Configuration[configKey];
    return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : fallback;
}

void ConfigureTrustedForwardedHeaderSources(ForwardedHeadersOptions options)
{
    var trustAll = string.Equals(
        Environment.GetEnvironmentVariable("TRUST_ALL_FORWARDED_HEADERS"),
        "true",
        StringComparison.OrdinalIgnoreCase);

    if (trustAll)
    {
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        return;
    }

    var proxyIps = SplitCsv(Environment.GetEnvironmentVariable("TRUSTED_PROXY_IPS"));
    var proxyNetworks = SplitCsv(Environment.GetEnvironmentVariable("TRUSTED_PROXY_NETWORKS"));

    if (proxyIps.Count == 0 && proxyNetworks.Count == 0)
        return;

    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();

    foreach (var proxyIp in proxyIps)
    {
        if (IPAddress.TryParse(proxyIp, out var ip))
            options.KnownProxies.Add(ip);
    }

    foreach (var proxyNetwork in proxyNetworks)
    {
        if (System.Net.IPNetwork.TryParse(proxyNetwork, out var network))
        {
            options.KnownIPNetworks.Add(network);
        }
    }
}

static IReadOnlyList<string> SplitCsv(string? raw) =>
    string.IsNullOrWhiteSpace(raw)
        ? []
        : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
