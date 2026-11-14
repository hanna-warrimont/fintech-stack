using Confluent.Kafka;
using Portal.Messaging;
using Portal.Messaging.Contracts;

namespace Portal.History;

/// <summary>
/// Liest <c>ticks.v1</c> in die Kerzen. Arbeitsteilung: alle History-Instanzen teilen die Group <c>history</c>,
/// jede bekommt einen Teil der Partitionen (Gegenstück zu Quote, wo jede Instanz alles bekommt).
/// Commit manuell und erst, wenn der Tick gespeichert ODER im DLT ist – so geht keiner verloren.
/// </summary>
public sealed class TickConsumer(
    CandleStore store, DeadLetter deadLetter, ConsumerConfig config, ILogger<TickConsumer> log, string topic = Topics.Ticks)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken ct) => Task.Run(() => RunAsync(ct), ct);

    private async Task RunAsync(CancellationToken ct)
    {
        // Leere Kerzen-Tabelle = Neuaufbau aus ticks.v1 ab Beginn (soweit die Retention reicht).
        // ponytail: Neuaufbau nur mit einer Instanz sicher; sonst die Offsets der Group zurücksetzen (kafka-consumer-groups --reset-offsets)
        var rebuild = await store.IsEmptyAsync(ct);
        if (rebuild)
        {
            await store.ClearAsync(ct); // auch instrument_seq – sonst verwürfe die Dedupe alle alten Ticks
            log.LogInformation("Kerzen leer – Neuaufbau aus {Topic} ab Beginn", topic);
        }

        using var consumer = new ConsumerBuilder<string, string>(config)
            .SetPartitionsAssignedHandler((_, partitions) =>
            {
                var offset = rebuild ? Offset.Beginning : Offset.Unset; // Unset = beim committeten Offset weitermachen
                rebuild = false;
                return partitions.Select(p => new TopicPartitionOffset(p, offset));
            })
            .Build();
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
                try
                {
                    if (Tick.TryParse(result.Message.Value) is { } tick)
                        await deadLetter.HandleAsync(result, () => store.ApplyAsync(tick, ct), ct); // Duplikat = false, kein Fehler
                    else
                        await deadLetter.SendAsync(result, new FormatException("Kein gültiger Tick"), 1, ct); // Poison: Retry sinnlos
                    consumer.Commit(result);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // Nicht einmal das DLT ging (Kafka weg): nicht committen, zurückspulen, später erneut versuchen.
                    log.LogError(e, "Tick weder gespeichert noch im DLT, neuer Versuch: {TopicPartitionOffset}", result.TopicPartitionOffset);
                    consumer.Seek(result.TopicPartitionOffset);
                    await Task.Delay(1000, ct);
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
