using System.Globalization;
using Microsoft.AspNetCore.Authentication;

namespace Portal.Bff;

/// <summary>
/// Die Cookie-Session lebt nur so lange wie das Access Token darin. Danach würde jeder Aufruf mit einem abgelaufenen
/// Token weitergereicht (401), während die UI noch „angemeldet“ zeigt – also Session beenden und neu anmelden lassen.
/// </summary>
// ponytail: kein Token-Refresh; Ausbau: refresh_token gegen Keycloak tauschen, bevor expires_at erreicht ist
public static class Session
{
    public static bool IsExpired(AuthenticationProperties properties, DateTimeOffset now) =>
        !DateTimeOffset.TryParse(properties.GetTokenValue("expires_at"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiresAt)
        || expiresAt <= now;
}
