using System.Text.Json;
using Portal.Instruments.Domain.CorporateActions;
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
        if (!instrument.DomainEvents.Any(e => e is InstrumentCreated or InstrumentRenamed or InstrumentIsinChanged or InstrumentDelisted))
            return null;

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

    /// <summary>
    /// Kapitalmaßnahmen sind Fakten, keine Zustände: Jedes Event wird eine eigene Nachricht (anders als der Snapshot,
    /// bei dem nur der letzte Stand zählt). Key = InstrumentId, damit alle Fakten eines Instruments geordnet ankommen.
    /// </summary>
    public static IEnumerable<OutboxMessage> ToOutbox(CorporateAction action, DateTimeOffset now, string? traceParent)
    {
        var payload = new CorporateActionEvent(action.Id, action.InstrumentId, action.Type.ToString(),
            action.Ratio?.Old, action.Ratio?.New, action.NewIsin?.Value, action.EffectiveAt);

        foreach (var domainEvent in action.DomainEvents)
        {
            var type = domainEvent switch
            {
                CorporateActionApproved => CorporateActionEvent.Approved,
                CorporateActionRejected => CorporateActionEvent.Rejected,
                CorporateActionCancelled => CorporateActionEvent.Cancelled,
                CorporateActionBecameEffective => CorporateActionEvent.BecameEffective,
                _ => null,
            };
            if (type is null) continue;

            var messageId = Guid.CreateVersion7();
            yield return new OutboxMessage
            {
                Id = messageId,
                Topic = Topics.CorporateActions,
                Key = action.InstrumentId.ToString("D"),
                Type = type,
                Payload = JsonSerializer.Serialize(
                    new Envelope<CorporateActionEvent>(messageId, type, CorporateActionEvent.MessageVersion, now, payload),
                    JsonSerializerOptions.Web),
                OccurredAt = now,
                TraceParent = traceParent,
            };
        }
    }
}
