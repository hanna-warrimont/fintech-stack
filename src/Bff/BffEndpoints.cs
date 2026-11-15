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

        app.MapGet("/api/pdp/{id:guid}", Pdp);
    }

    public const string InstrumentClient = "instrument";

    /// <summary>
    /// Dieselbe Geduld wie YARP für den Instrument-Dienst (ActivityTimeout in appsettings.json). Ohne eigenes Limit gälte
    /// der Standard-Resilience-Handler aus ServiceDefaults mit bis zu 30 s – die PDP hinge, während der Proxy längst 504 sagt.
    /// </summary>
    // ponytail: ein Limit um den ganzen Aufruf statt einer eigenen Resilience-Pipeline pro Client; die Retries des
    // Standard-Handlers laufen innerhalb dieser 5 s weiter. Eigene Pipeline erst, wenn das BFF mehr Backend-Aufrufe macht.
    public static readonly TimeSpan PdpTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// PDP-Aggregation: ein Aufruf aus dem Browser, zwei parallel im Backend. Beide werden abgewartet – auch wenn das
    /// Instrument fehlt –, damit kein Fehler unbeobachtet verpufft. Fällt der Instrument-Dienst aus, antwortet das BFF
    /// mit einem Problem (504 nach <see cref="PdpTimeout"/>, sonst 502) statt mit einem nackten 500.
    /// </summary>
    public static async Task<IResult> Pdp(Guid id, IHttpClientFactory clients, ILogger<PdpLog> log, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(PdpTimeout);
        var http = clients.CreateClient(InstrumentClient);
        var instrument = http.GetAsync($"/instruments/{id}", timeout.Token);
        var actions = http.GetFromJsonAsync<JsonElement>($"/instruments/{id}/corporate-actions", timeout.Token);
        try
        {
            await Task.WhenAll(instrument, actions);
            using var response = await instrument;
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return Results.NotFound();
            response.EnsureSuccessStatusCode();
            return Results.Ok(new
            {
                instrument = await response.Content.ReadFromJsonAsync<JsonElement>(timeout.Token),
                corporateActions = await actions,
            });
        }
        catch (Exception e) when (!ct.IsCancellationRequested) // Abbruch durch den Browser bleibt ein Abbruch
        {
            log.LogWarning(e, "PDP {InstrumentId}: Instrument-Dienst nicht verfügbar", id);
            return timeout.IsCancellationRequested
                ? Results.Problem("Der Instrument-Dienst antwortet nicht rechtzeitig.", statusCode: StatusCodes.Status504GatewayTimeout)
                : Results.Problem("Der Instrument-Dienst ist nicht verfügbar.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>Kategorie für die Logs der PDP-Aggregation (statische Klassen taugen nicht als Typargument).</summary>
    public sealed class PdpLog;

    /// <summary>
    /// Nur lokale Pfade – sonst wäre der Login ein offener Redirect auf fremde Seiten. Keine Steuerzeichen:
    /// Browser entfernen Tab/Zeilenumbruch, aus „/\t/evil.example“ würde „//evil.example“.
    /// </summary>
    public static string SafeReturnUrl(string? returnUrl) =>
        returnUrl is ['/', not '/' and not '\\', ..] or "/" && !returnUrl.Any(char.IsControl) ? returnUrl : "/";
}
