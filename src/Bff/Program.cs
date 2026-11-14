using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Portal.Bff;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Der Browser spricht mit Nuxt, Nuxt reicht /api hierher durch: Die Redirect-URI für Keycloak muss den Host des
// Browsers tragen, nicht den des BFF.
builder.Services.Configure<ForwardedHeadersOptions>(o => o.ForwardedHeaders = ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto);

// BFF-Muster: Login (Authorization Code) läuft hier, der Browser bekommt nur ein HttpOnly-Cookie mit Session-Schlüssel.
builder.Services.AddAuthentication(o =>
    {
        o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(o =>
    {
        o.Cookie.Name = "portal.session";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.SessionStore = new MemoryTicketStore();
    })
    .AddOpenIdConnect(o =>
    {
        o.Authority = $"{builder.Configuration["services:keycloak:http:0"]}/realms/portal";
        o.ClientId = "portal-bff";
        o.ClientSecret = builder.Configuration["Bff:ClientSecret"];
        o.ResponseType = OpenIdConnectResponseType.Code;
        // Query statt form_post: Rückkehr per Top-Level-GET, damit Correlation- und Nonce-Cookie mit SameSite=Lax über HTTP gehen.
        o.ResponseMode = OpenIdConnectResponseMode.Query;
        o.CorrelationCookie.SameSite = SameSiteMode.Lax;
        o.NonceCookie.SameSite = SameSiteMode.Lax;
        o.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        o.SaveTokens = true; // landen im MemoryTicketStore, nicht im Cookie
        o.CallbackPath = "/api/signin-oidc";
        o.SignedOutCallbackPath = "/api/signout-callback-oidc";
        o.MapInboundClaims = false;
        o.TokenValidationParameters.NameClaimType = "preferred_username";
        o.TokenValidationParameters.RoleClaimType = "roles";
    });
builder.Services.AddAuthorization();
builder.Services.AddHttpClient("instrument", c => c.BaseAddress = new Uri("http://instrument"));

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver()
    .AddTransforms(context =>
    {
        context.AddRequestTransform(async t =>
        {
            // Das Session-Cookie geht keinen Backend-Dienst etwas an.
            t.ProxyRequest.Headers.Remove("Cookie");
            // Token-Weitergabe: Das BFF hängt das Access Token an – der Instrument-Service prüft Signatur und Rollen selbst.
            if (context.Cluster?.ClusterId == "instrument" && await t.HttpContext.GetTokenAsync("access_token") is { } token)
                t.ProxyRequest.Headers.Authorization = new("Bearer", token);
        });
    });

var app = builder.Build();

app.UseForwardedHeaders();
app.UseAuthentication(); // bearbeitet auch die Rückkehr von Keycloak (/api/signin-oidc)
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && !Csrf.IsAllowed(context.Request))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("CSRF-Header fehlt");
        return;
    }
    await next();
});

app.MapBffEndpoints();
app.MapReverseProxy();
app.MapDefaultEndpoints();

app.Run();
