using System.Text.Json;
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
                if (Parse(result.Message.Value) is { } snapshot)
                    catalog.Apply(snapshot);
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

    private static InstrumentSnapshot? Parse(string? value)
    {
        if (value is null) return null;
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope<InstrumentSnapshot>>(value, JsonSerializerOptions.Web);
            return envelope is { Type: InstrumentSnapshot.MessageType } ? envelope.Payload : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
