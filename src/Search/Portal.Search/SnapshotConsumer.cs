using Confluent.Kafka;
using Portal.Messaging;
using Portal.Messaging.Contracts;

namespace Portal.Search;

/// <summary>
/// Liest <c>instruments.v1</c> in die Projektion. Arbeitsteilung: alle Search-Instanzen teilen die Group <c>search</c>,
/// jede bekommt einen Teil der Partitionen. Offsets werden manuell committet – erst NACH dem Speichern (at-least-once).
/// </summary>
public sealed class SnapshotConsumer(
    InstrumentProjection projection, ConsumerConfig config, ILogger<SnapshotConsumer> log, string topic = Topics.Instruments)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken ct) => Task.Run(() => RunAsync(ct), ct);

    private async Task RunAsync(CancellationToken ct)
    {
        // Leere Projektion = Neuaufbau: alles aus dem kompaktierten Topic von vorn lesen.
        // ponytail: nur mit einer Search-Instanz sicher; sonst die Offsets der Group zurücksetzen (kafka-consumer-groups --reset-offsets)
        var rebuild = await projection.IsEmptyAsync(ct);
        if (rebuild)
        {
            await projection.ClearAsync(ct);
            log.LogInformation("Projektion leer – Neuaufbau aus {Topic} ab Beginn", topic);
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
                if (InstrumentSnapshot.TryParse(result.Message.Value) is not { } envelope)
                {
                    // ponytail: kein DLT bis Runde 3 – überspringen und committen, damit die Partition nicht hängt
                    log.LogWarning("Ungültige Nachricht übersprungen: {TopicPartitionOffset}", result.TopicPartitionOffset);
                    consumer.Commit(result);
                    continue;
                }

                try
                {
                    if (!await projection.ApplyAsync(envelope, ct))
                        log.LogInformation("Nachricht {MessageId} bereits verarbeitet", envelope.MessageId);
                    consumer.Commit(result); // erst jetzt: Absturz davor = Nachricht kommt noch einmal (Inbox fängt das ab)
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // Nicht committen und zurückspulen: dieselbe Nachricht wird erneut versucht statt übersprungen.
                    log.LogWarning(e, "Projektion fehlgeschlagen, neuer Versuch: {TopicPartitionOffset}", result.TopicPartitionOffset);
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
