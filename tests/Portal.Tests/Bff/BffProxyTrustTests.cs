using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portal.Bff;

namespace Portal.Tests.Bff;

/// <summary>
/// Durch die echte ForwardedHeaders-Middleware: Aus Host und Schema, die das BFF danach sieht, baut es die Redirect-URI für
/// Keycloak. Geprüft wird also das Verhalten, nicht der Inhalt der Optionslisten.
/// </summary>
public sealed class BffProxyTrustTests
{
    [Fact]
    public async Task Loopback_proxy_is_trusted_by_default() =>
        Assert.Equal("https://portal.example", await OriginSeenBehind("127.0.0.1", trustAllProxies: false));

    [Fact]
    public async Task Other_senders_are_not_trusted_by_default() =>
        Assert.Equal("http://bff:8080", await OriginSeenBehind("10.0.0.5", trustAllProxies: false));

    [Fact]
    public async Task Any_sender_is_trusted_when_configured() =>
        Assert.Equal("https://portal.example", await OriginSeenBehind("10.0.0.5", trustAllProxies: true));

    private static async Task<string> OriginSeenBehind(string proxyIp, bool trustAllProxies)
    {
        var options = new ForwardedHeadersOptions();
        ProxyTrust.Configure(options, trustAllProxies);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(proxyIp);
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("bff:8080");
        context.Request.Headers["X-Forwarded-Host"] = "portal.example"; // so setzt es der Nuxt-Proxy
        context.Request.Headers["X-Forwarded-Proto"] = "https";

        await new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(options)).Invoke(context);

        return $"{context.Request.Scheme}://{context.Request.Host}";
    }
}
