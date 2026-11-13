using Confluent.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using Portal.Messaging;
using Portal.Messaging.Contracts;
using Portal.Quote;
using Portal.Simulator;

namespace Portal.Tests.Quote;

[Collection("kafka")]
public sealed class TickConsumerTests(KafkaFixture kafka)
{
    private static readonly Guid A = Guid.NewGuid();

    [Fact]
    public async Task Two_instances_with_own_groups_both_receive_every_tick()
    {
        var topic = await NewTopic();
        var hubs = new[] { new QuoteHub(), new QuoteHub() };
        var consumers = hubs.Select(h => Consumer(h, topic, $"quote-{Guid.NewGuid():N}", AutoOffsetReset.Latest)).ToList();
        foreach (var c in consumers) await c.StartAsync(CancellationToken.None);

        try
        {
            using var producer = Producer();
            var publisher = new TickPublisher(producer, topic);
            var deadline = DateTime.UtcNow.AddSeconds(30);
            for (var seq = 1; hubs.Any(h => h.Latest(A) is null) && DateTime.UtcNow < deadline; seq++)
            {
                await publisher.PublishAsync(new Tick(A, seq, 100m, 1, DateTimeOffset.UtcNow), CancellationToken.None);
                await Task.Delay(200);
            }

            Assert.All(hubs, h => Assert.NotNull(h.Latest(A)));
        }
        finally
        {
            foreach (var c in consumers) await c.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Consumer_skips_malformed_message()
    {
        var topic = await NewTopic();
        using (var producer = Producer())
        {
            await producer.ProduceAsync(topic, new Message<string, string> { Key = A.ToString("D"), Value = "kein json" });
            await new TickPublisher(producer, topic).PublishAsync(new Tick(A, 1, 100m, 1, DateTimeOffset.UtcNow), CancellationToken.None);
        }

        var hub = new QuoteHub();
        var consumer = Consumer(hub, topic, $"quote-{Guid.NewGuid():N}", AutoOffsetReset.Earliest);
        await consumer.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (hub.Latest(A) is null && DateTime.UtcNow < deadline) await Task.Delay(200);

            Assert.Equal(1, hub.Latest(A)?.Seq);
            Assert.False(consumer.ExecuteTask!.IsFaulted);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    private async Task<string> NewTopic()
    {
        var topic = $"ticks-test-{Guid.NewGuid():N}";
        await Topics.EnsureTopicAsync(kafka.BootstrapServers, topic);
        return topic;
    }

    private IProducer<string, string> Producer() =>
        new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build();

    private TickConsumer Consumer(QuoteHub hub, string topic, string groupId, AutoOffsetReset reset) =>
        new(hub, new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = reset,
            EnableAutoCommit = false,
        }, NullLogger<TickConsumer>.Instance, topic);
}
