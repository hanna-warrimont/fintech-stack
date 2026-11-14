using Microsoft.AspNetCore.HttpOverrides;

namespace Portal.Bff;

/// <summary>
/// Wem glaubt das BFF X-Forwarded-Host/Proto? Daraus baut es die Redirect-URI für Keycloak.
/// Lokal nur Loopback (Nuxt läuft auf derselben Maschine). In Azure kommt der Nuxt-Proxy aus dem internen Netz der
/// Container-Apps-Umgebung; das BFF hat dort nur internen Ingress, also darf es allen Absendern glauben.
/// </summary>
public static class ProxyTrust
{
    public static void Configure(ForwardedHeadersOptions options, bool trustAllProxies)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto;
        if (!trustAllProxies) return;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    }
}
