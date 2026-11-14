using Microsoft.AspNetCore.Builder;
using Portal.Bff;

namespace Portal.Tests.Bff;

public sealed class BffProxyTrustTests
{
    [Fact]
    public void Trusts_only_loopback_by_default()
    {
        var options = new ForwardedHeadersOptions();
        var networks = options.KnownIPNetworks.Count;
        var proxies = options.KnownProxies.Count;

        ProxyTrust.Configure(options, trustAllProxies: false);

        Assert.True(networks + proxies > 0);
        Assert.Equal((networks, proxies), (options.KnownIPNetworks.Count, options.KnownProxies.Count));
    }

    [Fact]
    public void Trusts_all_proxies_when_configured()
    {
        var options = new ForwardedHeadersOptions();

        ProxyTrust.Configure(options, trustAllProxies: true);

        Assert.Empty(options.KnownIPNetworks);
        Assert.Empty(options.KnownProxies);
    }
}
