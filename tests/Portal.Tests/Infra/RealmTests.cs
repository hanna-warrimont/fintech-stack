using System.Text.Json.Nodes;

namespace Portal.Tests.Infra;

/// <summary>
/// Zwei Realm-Dateien: lokal (src/AppHost/keycloak, feste Dev-Werte) und Azure (infra/keycloak, Platzhalter aus Secrets).
/// Sie dürfen sich nur in diesen Werten unterscheiden – sonst testet man lokal einen anderen Keycloak, als in Azure läuft.
/// </summary>
public sealed class RealmTests
{
    private static readonly JsonObject Local = Load("src/AppHost/keycloak/portal-realm.json");
    private static readonly JsonObject Azure = Load("infra/keycloak/portal-realm.json");

    [Fact]
    public void Local_and_azure_realm_differ_only_in_environment_values()
    {
        Assert.True(JsonNode.DeepEquals(WithoutEnvironmentValues(Local), WithoutEnvironmentValues(Azure)),
            "Die Realm-Dateien sind auseinandergelaufen – Änderung in beiden nachziehen.");
    }

    [Theory]
    [InlineData("src/AppHost/keycloak/portal-realm.json")]
    [InlineData("infra/keycloak/portal-realm.json")]
    public void Realm_is_hardened(string path)
    {
        var realm = Load(path);
        var client = realm["clients"]!.AsArray().Single(c => (string?)c!["clientId"] == "portal-bff")!;

        Assert.True((bool?)realm["bruteForceProtected"]);
        Assert.Equal("S256", (string?)client["attributes"]!["pkce.code.challenge.method"]);
        Assert.False((bool?)client["directAccessGrantsEnabled"]); // kein Passwort-Login an der Anmeldeseite vorbei
    }

    /// <summary>Entfernt, was je Umgebung anders sein MUSS: Geheimnisse, Adressen, Passwörter, TLS-Pflicht und Passwortregel.</summary>
    private static JsonObject WithoutEnvironmentValues(JsonObject realm)
    {
        var copy = realm.DeepClone().AsObject();
        copy.Remove("sslRequired");     // lokal HTTP ohne Zertifikat
        copy.Remove("passwordPolicy");  // lokal heißen die Passwörter wie die Nutzer
        foreach (var client in copy["clients"]!.AsArray())
        {
            client!.AsObject().Remove("secret");
            client.AsObject().Remove("redirectUris");
            client["attributes"]!.AsObject().Remove("post.logout.redirect.uris");
        }
        foreach (var user in copy["users"]!.AsArray())
            foreach (var credential in user!["credentials"]!.AsArray())
                credential!.AsObject().Remove("value");
        return copy;
    }

    private static JsonObject Load(string path)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "Portal.slnx"))) root = root.Parent!;
        return JsonNode.Parse(File.ReadAllText(Path.Combine(root.FullName, path)))!.AsObject();
    }
}
