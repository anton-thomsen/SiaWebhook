using System.Net;
using Microsoft.AspNetCore.Builder;
using SiaWebhookApi.Configuration;

namespace SiaWebhookApi.Tests;

/// <summary>
/// Forwarded headers must be trusted only from configured proxy sources —
/// otherwise any direct caller can spoof X-Forwarded-For and bypass per-IP
/// rate limiting on the webhook and the login endpoint.
/// </summary>
public class TrustedProxyConfiguratorTests
{
    [Fact]
    public void NothingConfigured_KeepsSafeLoopbackOnlyDefaults()
    {
        var options = new ForwardedHeadersOptions();

        TrustedProxyConfigurator.Configure(options, trustAll: null, proxyIpsCsv: null, proxyNetworksCsv: null);

        // ASP.NET Core's default trusts only the loopback proxy — that default
        // must survive when no explicit trust is configured.
        Assert.Contains(IPAddress.IPv6Loopback, options.KnownProxies);
    }

    [Fact]
    public void ConfiguredProxyIps_ReplaceDefaults()
    {
        var options = new ForwardedHeadersOptions();

        TrustedProxyConfigurator.Configure(options, null, "172.18.0.2, 172.18.0.3", null);

        Assert.Equal(2, options.KnownProxies.Count);
        Assert.Contains(IPAddress.Parse("172.18.0.2"), options.KnownProxies);
        Assert.Contains(IPAddress.Parse("172.18.0.3"), options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
    }

    [Fact]
    public void ConfiguredProxyNetworks_ReplaceDefaults()
    {
        var options = new ForwardedHeadersOptions();

        TrustedProxyConfigurator.Configure(options, null, null, "172.18.0.0/16");

        var network = Assert.Single(options.KnownIPNetworks);
        Assert.Equal(System.Net.IPNetwork.Parse("172.18.0.0/16"), network);
        Assert.Empty(options.KnownProxies);
    }

    [Fact]
    public void UnparseableEntries_AreSkippedNotTrusted()
    {
        var options = new ForwardedHeadersOptions();

        TrustedProxyConfigurator.Configure(options, null, "not-an-ip, 172.18.0.2", "not-a-cidr");

        var proxy = Assert.Single(options.KnownProxies);
        Assert.Equal(IPAddress.Parse("172.18.0.2"), proxy);
        Assert.Empty(options.KnownIPNetworks);
    }

    [Fact]
    public void TrustAllOptIn_ClearsAllRestrictions()
    {
        var options = new ForwardedHeadersOptions();

        TrustedProxyConfigurator.Configure(options, "true", null, null);

        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
    }

    [Fact]
    public void TrustAllRequiresExactOptIn()
    {
        var options = new ForwardedHeadersOptions();

        TrustedProxyConfigurator.Configure(options, "yes", null, null);

        // Anything other than "true" must not disable the protection.
        Assert.NotEmpty(options.KnownProxies);
    }
}
