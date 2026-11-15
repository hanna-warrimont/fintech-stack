using Confluent.Kafka;
using Portal.Messaging;
using Portal.Messaging.Contracts;

namespace Portal.History;

/// <summary>
/// Liest <c>ticks.v1</c> in die Kerzen. Arbeitsteilung: alle History-Instanzen teilen die Group <c>history</c>,
/// jede bekommt einen Teil der Partitionen (Gegenstück zu Quote, wo jede Instanz alles bekommt).
/// Offset erst merken, wenn der Tick gespeichert ODER im DLT ist – so geht keiner verloren.
/// <para>
/// Commit: anders als Search nicht synchron pro Nachricht (ein Broker-Roundtrip pro Tick deckelt den Durchsatz), sondern
/// das Confluent-Muster <c>StoreOffset</c> + Auto-Commit: Nach der Verarbeitung wird der Offset nur lokal gemerkt, der
/// Client committet alle 5 s und beim Rebalance oder Schließen. Weiter at-least-once: Gemerkt wird nie vor der Wirkung.
/// Nach einem Absturz kommen bis zu 5 s Ticks noch einmal – die seq-Dedupe im <see cref="CandleStore"/> verwirft sie.
/// </para>
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

        // Die Commit-Strategie gehört zu diesem Consumer, nicht zum Aufrufer: StoreOffset funktioniert nur ohne
        // automatisches Merken beim Konsumieren (das würde VOR der Verarbeitung merken).
        var storeThenAutoCommit = new ConsumerConfig(config) { EnableAutoCommit = true, EnableAutoOffsetStore = false };
        using var consumer = new ConsumerBuilder<string, string>(storeThenAutoCommit)
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
                    consumer.StoreOffset(result); // committet wird gesammelt (Auto-Commit), siehe Klassenkommentar
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // DB vorübergehend weg oder nicht einmal das DLT ging (Kafka weg): Offset nicht merken, zurückspulen,
                    // später erneut versuchen. Die Partition steht so lange – besser als Lücken in den Kerzen.
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
