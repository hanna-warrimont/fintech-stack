using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Portal.Bff;

public static class BffEndpoints
{
    public static void MapBffEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/bff/login", (string? returnUrl) => Results.Challenge(
            new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) }, [OpenIdConnectDefaults.AuthenticationScheme]));

        // ponytail: GET-Logout; erzwungenes Abmelden per fremdem Link ist harmlos
        app.MapGet("/api/bff/logout", () => Results.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));

        // Das Frontend fragt hier, wer angemeldet ist – Tokens bekommt es nie.
        app.MapGet("/api/bff/user", (ClaimsPrincipal user) => new
        {
            isAuthenticated = user.Identity?.IsAuthenticated ?? false,
            name = user.Identity?.Name,
            roles = user.FindAll("roles").Select(c => c.Value),
        });

        // PDP-Aggregation: ein Aufruf aus dem Browser, zwei parallel im Backend.
        app.MapGet("/api/pdp/{id:guid}", async (Guid id, IHttpClientFactory clients, CancellationToken ct) =>
        {
            var http = clients.CreateClient("instrument");
            var instrument = http.GetAsync($"/instruments/{id}", ct);
            var actions = http.GetFromJsonAsync<JsonElement>($"/instruments/{id}/corporate-actions", ct);
            using var response = await instrument;
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return Results.NotFound();
            response.EnsureSuccessStatusCode();
            return Results.Ok(new
            {
                instrument = await response.Content.ReadFromJsonAsync<JsonElement>(ct),
                corporateActions = await actions,
            });
        });
    }

    /// <summary>
    /// Nur lokale Pfade – sonst wäre der Login ein offener Redirect auf fremde Seiten. Keine Steuerzeichen:
    /// Browser entfernen Tab/Zeilenumbruch, aus „/\t/evil.example“ würde „//evil.example“.
    /// </summary>
    public static string SafeReturnUrl(string? returnUrl) =>
        returnUrl is ['/', not '/' and not '\\', ..] or "/" && !returnUrl.Any(char.IsControl) ? returnUrl : "/";
}
