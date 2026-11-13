using System.Diagnostics;
using System.Text.Json;
using Confluent.Kafka;
using Portal.Messaging;
using Portal.Messaging.Contracts;
using Portal.Simulator;

namespace Portal.Tests.Simulator;

[Collection("kafka")]
public sealed class TickPublisherTests(KafkaFixture kafka)
{
    [Fact]
    public async Task Ticks_of_one_instrument_land_on_one_partition_in_order()
    {
        // Wie das OTel-SDK im echten Betrieb: Ohne Listener entsteht keine Activity, also auch kein traceparent.
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == KafkaTracing.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var topic = $"ticks-test-{Guid.NewGuid():N}";
        await Topics.EnsureTopicAsync(kafka.BootstrapServers, topic);
        Guid[] instruments = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        using (var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build())
        {
            var publisher = new TickPublisher(producer, topic);
            for (var seq = 1; seq <= 20; seq++)
                foreach (var id in instruments) // abwechselnd über alle Instrumente
                    await publisher.PublishAsync(new Tick(id, seq, 100m, 1, DateTimeOffset.UtcNow), CancellationToken.None);
        }

        var received = Consume(topic, expected: 60);

        Assert.All(received, r => Assert.NotNull(r.Message.Headers.FirstOrDefault(h => h.Key == "traceparent")));
        foreach (var id in instruments)
        {
            var mine = received.Where(r => r.Message.Key == id.ToString("D")).ToList();
            Assert.Single(mine.Select(r => r.Partition.Value).Distinct());
            Assert.Equal(Enumerable.Range(1, 20).Select(i => (long)i),
                mine.Select(r => JsonSerializer.Deserialize<Tick>(r.Message.Value, JsonSerializerOptions.Web)!.Seq));
        }
    }

    private List<ConsumeResult<string, string>> Consume(string topic, int expected)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"test-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(topic);

        var results = new List<ConsumeResult<string, string>>();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (results.Count < expected && DateTime.UtcNow < deadline)
        {
            if (consumer.Consume(TimeSpan.FromMilliseconds(500)) is { } r) results.Add(r);
        }

        consumer.Close();
        Assert.Equal(expected, results.Count);
        return results;
    }
}
