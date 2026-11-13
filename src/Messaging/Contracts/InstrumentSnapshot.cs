using System.Text.Json;

namespace Portal.Messaging.Contracts;

/// <summary>
/// Integration Event auf <c>instruments.v1</c>: der komplette aktuelle Stand eines Instruments (Event-carried State Transfer).
/// Strings statt Domänen-Enums: Der Vertrag zwischen Services soll nicht an die Domäne eines Services gekoppelt sein.
/// <see cref="Revision"/> steigt mit jeder Änderung – Consumer verwerfen ältere Snapshots.
/// </summary>
public sealed record InstrumentSnapshot(
    Guid Id, string Isin, string? Wkn, string Name, string Type, string Currency, string Status, int Revision)
{
    public const string MessageType = "InstrumentSnapshot";
    public const int MessageVersion = 1;

    // Streng: fehlende Felder oder null in Nicht-null-Feldern sind ein Fehler – sonst würde aus {"payload":{}}
    // ein Snapshot voller Defaults, der beim Speichern scheitert (Partition hängt) oder Consumer abstürzen lässt.
    private static readonly JsonSerializerOptions Strict = new(JsonSerializerOptions.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    /// <summary>Liest einen Umschlag von <c>instruments.v1</c>; <c>null</c> bei Tombstone, fremdem Typ oder ungültigem Inhalt.</summary>
    public static Envelope<InstrumentSnapshot>? TryParse(string? value)
    {
        if (value is null) return null; // Tombstone
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope<InstrumentSnapshot>>(value, Strict);
            return envelope is { Type: MessageType, Payload: not null } ? envelope : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
