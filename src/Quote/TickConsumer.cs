using Confluent.Kafka;
using Portal.Messaging;
using Portal.Messaging.Contracts;

namespace Portal.Quote;

/// <summary>
/// Liest <c>ticks.v1</c> in den <see cref="QuoteHub"/>. Broadcast: Jede Quote-Instanz hat ihre eigene Group-ID
/// und bekommt deshalb ALLE Ticks – an jeder Instanz hängen eigene SSE-Clients.
/// (Gegenstück Arbeitsteilung: alle Instanzen teilen eine Group, jede bekommt nur einen Teil der Partitionen.)
/// </summary>
public sealed class TickConsumer(QuoteHub hub, ConsumerConfig config, ILogger<TickConsumer> log, string topic = Topics.Ticks)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken ct) => Task.Run(() => Run(ct), ct);

    private void Run(CancellationToken ct)
    {
        // ponytail: Broadcast-Group startet immer bei latest, Offsets sind bedeutungslos → kein Commit
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
                    // z. B. Topic (noch) nicht vorhanden oder gerade neu angelegt: weiterlaufen statt den Host zu beenden
                    log.LogWarning(e, "Konsumieren von {Topic} fehlgeschlagen, versuche weiter", topic);
                    continue;
                }

                using var activity = KafkaTracing.StartConsume(topic, result.Message.Headers);
                // ponytail: kein DLT – für die Live-Anzeige ist ein verlorener Tick egal, der nächste kommt gleich
                if (Tick.TryParse(result.Message.Value) is { } tick)
                    hub.Publish(tick);
                else
                    log.LogWarning("Ungültiger Tick übersprungen: {TopicPartitionOffset}", result.TopicPartitionOffset);
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
