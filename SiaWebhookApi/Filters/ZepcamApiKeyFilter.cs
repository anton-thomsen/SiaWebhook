using System.Security.Cryptography;
using System.Text;
using SiaWebhookApi.Configuration;

namespace SiaWebhookApi.Filters;

/// <summary>
/// Endpoint filter that gates the Zepcam webhook behind a static Bearer API key.
/// Zepcam is a machine client, so a long-lived shared secret (not a JWT login) is the
/// right model. The key is compared in constant time to avoid a timing side-channel.
/// </summary>
public class ZepcamApiKeyFilter : IEndpointFilter
{
    private const string BearerPrefix = "Bearer ";

    private readonly AuthOptions _options;
    private readonly ILogger<ZepcamApiKeyFilter> _logger;

    public ZepcamApiKeyFilter(AuthOptions options, ILogger<ZepcamApiKeyFilter> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        string header = context.HttpContext.Request.Headers.Authorization.ToString();

        if (!header.StartsWith(BearerPrefix, StringComparison.Ordinal)
            || !KeyMatches(header[BearerPrefix.Length..]))
        {
            _logger.LogWarning(
                "Rejected /zepcam-panic: missing or invalid API key (client {Ip})",
                context.HttpContext.Connection.RemoteIpAddress);
            return Results.Unauthorized();
        }

        return await next(context);
    }

    private bool KeyMatches(string provided)
    {
        Span<byte> a = stackalloc byte[32];
        Span<byte> b = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(provided), a);
        SHA256.HashData(Encoding.UTF8.GetBytes(_options.ZepcamApiKey), b);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
