namespace Portal.Instruments.Domain.CorporateActions;

/// <summary>
/// Wer einen Schritt ausführt. <see cref="Id"/> ist die unveränderliche Kennung aus dem Login (Keycloak-Claim <c>sub</c>)
/// und entscheidet die Vier-Augen-Regel. <see cref="Name"/> ist nur für Menschen: Ein Nutzername lässt sich umbenennen
/// und nach dem Löschen neu vergeben – als Identität taugt er nicht.
/// </summary>
public sealed record Actor(string Id, string Name)
{
    // Leere Werte sind ein Programmierfehler (fehlender Claim), kein fachlicher Fehler – und eine leere Id würde die
    // Vier-Augen-Regel aushebeln.
    public string Id { get; } = !string.IsNullOrWhiteSpace(Id) ? Id : throw new ArgumentException("Id fehlt.", nameof(Id));
    public string Name { get; } = !string.IsNullOrWhiteSpace(Name) ? Name : throw new ArgumentException("Name fehlt.", nameof(Name));
}
