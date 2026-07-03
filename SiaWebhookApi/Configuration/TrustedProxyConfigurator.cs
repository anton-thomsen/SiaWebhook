using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace SiaWebhookApi.Configuration;

/// <summary>
/// Decides which sources are trusted for X-Forwarded-For/Proto. Trusting the
/// headers from anywhere lets any direct caller spoof its client IP and bypass
/// per-IP rate limiting, so trust is limited to explicitly configured proxies.
/// </summary>
public static class TrustedProxyConfigurator
{
    public static void Configure(ForwardedHeadersOptions options) =>
        Configure(
            options,
            Environment.GetEnvironmentVariable("TRUST_ALL_FORWARDED_HEADERS"),
            Environment.GetEnvironmentVariable("TRUSTED_PROXY_IPS"),
            Environment.GetEnvironmentVariable("TRUSTED_PROXY_NETWORKS"));

    public static void Configure(
        ForwardedHeadersOptions options,
        string? trustAll,
        string? proxyIpsCsv,
        string? proxyNetworksCsv)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        // Escape hatch for setups where the container port is provably reachable
        // only by the proxy. Must be set explicitly and knowingly.
        if (string.Equals(trustAll, "true", StringComparison.OrdinalIgnoreCase))
        {
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            return;
        }

        var proxyIps = SplitCsv(proxyIpsCsv);
        var proxyNetworks = SplitCsv(proxyNetworksCsv);

        // Nothing configured: keep ASP.NET Core's safe default (loopback proxy only).
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
                options.KnownIPNetworks.Add(network);
        }
    }

    private static IReadOnlyList<string> SplitCsv(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
