using Confluent.Kafka;
using Portal.Messaging;
using Portal.Messaging.Contracts;

namespace Portal.Simulator;

/// <summary>
/// Reagiert auf wirksame Splits: Der laufende Kurs springt ins neue Niveau, neue Ticks kommen darin.
/// Broadcast wie der Katalog (eigene Group pro Instanz), Start bei latest: Ältere Splits stecken schon im Kurs.
/// Delisting braucht nichts Eigenes – der Snapshot mit Status Delisted nimmt das Instrument aus dem Katalog.
/// </summary>
public sealed class CorporateActionConsumer(
    PriceBook prices, ConsumerConfig config, ILogger<CorporateActionConsumer> log, string topic = Topics.CorporateActions)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken ct) => Task.Run(() => Run(ct), ct);

    private void Run(CancellationToken ct)
    {
        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(topic);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<string, string> result;
                try
                {
                    result = consumer.Consume(ct);
                }
                catch (ConsumeException e) when (!e.Error.IsFatal)
                {
                    log.LogWarning(e, "Konsumieren von {Topic} fehlgeschlagen, versuche weiter", topic);
                    continue;
                }

                using var activity = KafkaTracing.StartConsume(topic, result.Message.Headers);
                if (CorporateActionEvent.TryParse(result.Message.Value) is
                    { Type: CorporateActionEvent.BecameEffective, Payload: { Kind: "Split", SplitOld: { } old, SplitNew: { } @new } split })
                {
                    activity?.SetTag("corporateActionId", split.CorporateActionId);
                    prices.ApplySplit(split.InstrumentId, (decimal)old / @new);
                    log.LogInformation("Split {Old}:{New} für {InstrumentId} angewendet", old, @new, split.InstrumentId);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            consumer.Close();
        }
    }
}
