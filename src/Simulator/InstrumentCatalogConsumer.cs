using Confluent.Kafka;
using Portal.Messaging;
using Portal.Messaging.Contracts;

namespace Portal.Simulator;

/// <summary>
/// Liest <c>instruments.v1</c> in den <see cref="InstrumentCatalog"/>. Broadcast wie Quote: eigene Group pro Instanz,
/// immer ab Beginn (kompaktiert = aktueller Stand aller Instrumente), kein Commit.
/// </summary>
public sealed class InstrumentCatalogConsumer(
    InstrumentCatalog catalog, ConsumerConfig config, ILogger<InstrumentCatalogConsumer> log, string topic = Topics.Instruments)
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
                if (InstrumentSnapshot.TryParse(result.Message.Value) is { } envelope)
                    catalog.Apply(envelope.Payload);
                else
                    log.LogWarning("Ungültige Nachricht übersprungen: {TopicPartitionOffset}", result.TopicPartitionOffset);
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
