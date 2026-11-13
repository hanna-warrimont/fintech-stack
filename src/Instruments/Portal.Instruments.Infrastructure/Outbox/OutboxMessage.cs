namespace Portal.Instruments.Infrastructure.Outbox;

/// <summary>Eine Nachricht, die noch nach Kafka muss. Wird in derselben Transaktion wie die fachliche Änderung gespeichert.</summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }
    public required string Topic { get; init; }
    public required string Key { get; init; }
    public required string Type { get; init; }
    public required string Payload { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string? TraceParent { get; init; }
    public DateTimeOffset? ProcessedAt { get; init; }
}
