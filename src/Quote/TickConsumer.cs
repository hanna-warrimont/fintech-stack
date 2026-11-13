using System.Text.Json;
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
                var result = consumer.Consume(ct);
                using var activity = KafkaTracing.StartConsume(topic, result.Message.Headers);
                try
                {
                    if (JsonSerializer.Deserialize<Tick>(result.Message.Value, JsonSerializerOptions.Web) is { } tick)
                        hub.Publish(tick);
                    else
                        Skip(result, null);
                }
                catch (JsonException e)
                {
                    // ponytail: kein DLT, kommt in Runde 3
                    Skip(result, e);
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

    private void Skip(ConsumeResult<string, string> result, Exception? e) =>
        log.LogWarning(e, "Ungültiger Tick übersprungen: {TopicPartitionOffset}", result.TopicPartitionOffset);
}
