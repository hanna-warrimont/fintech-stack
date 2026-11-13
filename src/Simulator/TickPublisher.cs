using System.Text.Json;
using Confluent.Kafka;
using Portal.Messaging;
using Portal.Messaging.Contracts;

namespace Portal.Simulator;

public sealed class TickPublisher(IProducer<string, string> producer, string topic = Topics.Ticks)
{
    public async Task PublishAsync(Tick tick, CancellationToken ct)
    {
        var headers = new Headers();
        using var activity = KafkaTracing.StartPublish(topic, headers);

        await producer.ProduceAsync(topic, new Message<string, string>
        {
            // Key = InstrumentId, nicht ISIN: Kafka ordnet nur innerhalb einer Partition, und der Key bestimmt die Partition.
            // Eine ISIN kann sich durch eine Kapitalmaßnahme ändern – dann landeten Ticks desselben Instruments
            // in einer anderen Partition, und die Reihenfolge pro Instrument wäre nicht mehr garantiert.
            Key = tick.InstrumentId.ToString("D"),
            Value = JsonSerializer.Serialize(tick, JsonSerializerOptions.Web),
            Headers = headers,
        }, ct);
    }
}
