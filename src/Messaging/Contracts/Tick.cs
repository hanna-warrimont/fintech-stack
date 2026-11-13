namespace Portal.Messaging.Contracts;

/// <summary>Ein Kurs-Tick auf <c>ticks.v1</c>. Bewusst ohne Event-Umschlag (hohe Rate, Dedupe über <see cref="Seq"/>).</summary>
public sealed record Tick(Guid InstrumentId, long Seq, decimal Price, long Volume, DateTimeOffset EventTime);
