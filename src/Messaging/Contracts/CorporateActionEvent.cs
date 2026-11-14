using System.Text.Json;

namespace Portal.Messaging.Contracts;

/// <summary>
/// Integration Event auf <c>corporate-actions.v1</c>: ein fachlicher Fakt zu einer Kapitalmaßnahme.
/// Welcher Fakt, steht im <see cref="Envelope{T}.Type"/> (<see cref="Approved"/>, …). <see cref="Kind"/> = Split, IsinChange, Delisting.
/// </summary>
public sealed record CorporateActionEvent(
    Guid CorporateActionId, Guid InstrumentId, string Kind, int? SplitOld, int? SplitNew, string? NewIsin, DateTimeOffset EffectiveAt)
{
    public const string Approved = "CorporateActionApproved";
    public const string Rejected = "CorporateActionRejected";
    public const string Cancelled = "CorporateActionCancelled";
    public const string BecameEffective = "CorporateActionBecameEffective";
    public const int MessageVersion = 1;

    private static readonly string[] Types = [Approved, Rejected, Cancelled, BecameEffective];

    /// <summary>Liest einen Umschlag von <c>corporate-actions.v1</c>; <c>null</c> bei Tombstone, fremdem Typ oder ungültigem Inhalt.</summary>
    public static Envelope<CorporateActionEvent>? TryParse(string? value)
    {
        if (value is null) return null;
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope<CorporateActionEvent>>(value, StrictJson.Options);
            return envelope is { Payload: not null } && Types.Contains(envelope.Type) ? envelope : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
