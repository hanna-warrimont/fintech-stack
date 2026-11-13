using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using Portal.Messaging;
using Portal.Messaging.Contracts;
using Portal.Simulator;

namespace Portal.Tests.Simulator;

[Collection("infra")]
public sealed class InstrumentCatalogConsumerTests(KafkaFixture kafka)
{
    [Fact]
    public async Task Typed_but_invalid_payload_is_skipped()
    {
        var topic = $"instruments-test-{Guid.NewGuid():N}";
        await Topics.EnsureTopicAsync(kafka.BootstrapServers, topic, config: Topics.Compacted);
        var valid = new InstrumentSnapshot(Guid.NewGuid(), "DE0007164600", "716460", "SAP SE", "Share", "EUR", "Active", 1);
        using (var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build())
        {
            // ohne isin/name: StartPrices.For(null) ließ den Simulator abstürzen
            await producer.ProduceAsync(topic, new Message<string, string>
            {
                Key = "x",
                Value = """{"type":"InstrumentSnapshot","payload":{"id":"6939a8d3-111d-409b-897b-996093dd32f8","status":"Active","revision":1}}""",
            });
            await producer.ProduceAsync(topic, new Message<string, string>
            {
                Key = valid.Id.ToString("D"),
                Value = JsonSerializer.Serialize(new Envelope<InstrumentSnapshot>(Guid.NewGuid(), InstrumentSnapshot.MessageType, 1, DateTimeOffset.UtcNow, valid), JsonSerializerOptions.Web),
            });
        }

        var catalog = new InstrumentCatalog();
        var consumer = new InstrumentCatalogConsumer(catalog, new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"simulator-test-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }, NullLogger<InstrumentCatalogConsumer>.Instance, topic);
        await consumer.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (catalog.Active.All(i => i.Id != valid.Id) && DateTime.UtcNow < deadline) await Task.Delay(200);
            await Task.Delay(500);

            Assert.Equal([valid.Id], catalog.Active.Select(i => i.Id));
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }
}
