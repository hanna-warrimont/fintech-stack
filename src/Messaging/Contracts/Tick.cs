using System.Text.Json;

namespace Portal.Messaging.Contracts;

/// <summary>Ein Kurs-Tick auf <c>ticks.v1</c>. Bewusst ohne Event-Umschlag (hohe Rate, Dedupe über <see cref="Seq"/>).</summary>
public sealed record Tick(Guid InstrumentId, long Seq, decimal Price, long Volume, DateTimeOffset EventTime)
{
    /// <summary>Liest einen Tick von <c>ticks.v1</c>; <c>null</c> bei Tombstone oder ungültigem Inhalt (Poison).</summary>
    public static Tick? TryParse(string? value)
    {
        if (value is null) return null; // Tombstone
        try
        {
            return JsonSerializer.Deserialize<Tick>(value, StrictJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
