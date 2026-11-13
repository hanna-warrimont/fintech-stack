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
}
