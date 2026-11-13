using System.Text.Json;
using Portal.Instruments.Domain.Instruments;
using Portal.Messaging;
using Portal.Messaging.Contracts;

namespace Portal.Instruments.Infrastructure.Outbox;

/// <summary>
/// Domain Event → Integration Event. Domain Events sind feingranular und intern (<see cref="InstrumentCreated"/>,
/// <see cref="InstrumentRenamed"/>); nach außen geht EIN grober Snapshot mit dem kompletten aktuellen Stand
/// (Event-carried State Transfer). Mehrere Änderungen in einer Transaktion ergeben einen Snapshot.
/// </summary>
public static class IntegrationEventMapper
{
    public static OutboxMessage? ToOutbox(Instrument instrument, DateTimeOffset now, string? traceParent)
    {
        if (!instrument.DomainEvents.Any(e => e is InstrumentCreated or InstrumentRenamed)) return null;

        var messageId = Guid.CreateVersion7();
        var snapshot = new InstrumentSnapshot(
            instrument.Id, instrument.Isin.Value, instrument.Wkn?.Value, instrument.Name, instrument.Type.ToString(),
            instrument.Currency.Value, instrument.Status.ToString(), instrument.Revision);
        var envelope = new Envelope<InstrumentSnapshot>(
            messageId, InstrumentSnapshot.MessageType, InstrumentSnapshot.MessageVersion, now, snapshot);

        return new OutboxMessage
        {
            Id = messageId,
            Topic = Topics.Instruments,
            Key = instrument.Id.ToString("D"),
            Type = InstrumentSnapshot.MessageType,
            Payload = JsonSerializer.Serialize(envelope, JsonSerializerOptions.Web),
            OccurredAt = now,
            TraceParent = traceParent,
        };
    }
}
