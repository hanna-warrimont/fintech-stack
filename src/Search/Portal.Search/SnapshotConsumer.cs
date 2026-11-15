using Confluent.Kafka;
using Portal.Messaging;
using Portal.Messaging.Contracts;

namespace Portal.Search;

/// <summary>
/// Liest <c>instruments.v1</c> in die Projektion. Arbeitsteilung: alle Search-Instanzen teilen die Group <c>search</c>,
/// jede bekommt einen Teil der Partitionen. Offsets werden manuell committet – erst NACH dem Speichern (at-least-once).
/// Fehler, zwei Fälle mit verschiedener Antwort:
/// <list type="bullet">
/// <item>Nicht lesbar (Poison): sofort ins DLT <c>instruments.v1.search.dlt</c> – an einem kaputten Inhalt ändert kein Retry etwas.</item>
/// <item>Speichern scheitert: kein DLT, sondern wiederholen, bis es klappt. Die Projektion hat keine eigenen Regeln, die an einer
/// einzelnen Nachricht scheitern könnten – ein Fehler hier liegt an der Datenbank und träfe jede Nachricht. Im DLT wäre die
/// Suche still veraltet. (Anders History: Dort hält ein Stau bei hoher Tick-Rate mehr auf, als ein verlorener Tick schadet.)</item>
/// </list>
/// </summary>
public sealed class SnapshotConsumer(
    InstrumentProjection projection, DeadLetter deadLetter, ConsumerConfig config, ILogger<SnapshotConsumer> log,
    string topic = Topics.Instruments)
    : BackgroundService
{
    public const string Group = "search";

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
                try
                {
                    if (result.Message.Value is null)
                        // Tombstone: In einem kompaktierten Topic ein legitimes „gelöscht“, kein Poison. Löschen gibt es (noch) nicht.
                        log.LogInformation("Tombstone übersprungen: {TopicPartitionOffset}", result.TopicPartitionOffset);
                    else if (InstrumentSnapshot.TryParse(result.Message.Value) is not { } envelope)
                        await deadLetter.SendAsync(result, new FormatException("Kein gültiger Instrument-Snapshot"), 1, ct);
                    else if (!await projection.ApplyAsync(envelope, ct))
                        log.LogInformation("Nachricht {MessageId} bereits verarbeitet", envelope.MessageId);
                    consumer.Commit(result); // erst jetzt: Absturz davor = Nachricht kommt noch einmal (Inbox fängt das ab)
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // Nicht committen und zurückspulen: dieselbe Nachricht wird erneut versucht statt übersprungen –
                    // Datenbank weg, oder nicht einmal das DLT ging (Kafka weg).
                    log.LogWarning(e, "Weder gespeichert noch im DLT, neuer Versuch: {TopicPartitionOffset}", result.TopicPartitionOffset);
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
