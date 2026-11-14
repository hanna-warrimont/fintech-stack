using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using Portal.Messaging;

namespace Portal.Tests.Messaging;

[Collection("infra")]
public sealed class DeadLetterTests(KafkaFixture kafka)
{
    private const string Group = "history";

    [Fact]
    public async Task Succeeds_after_transient_failures_without_dlt()
    {
        var (topic, result) = await Setup();
        using var producer = Producer();
        var calls = 0;

        await new DeadLetter(producer, Group, NullLogger.Instance).HandleAsync(result, () =>
        {
            if (++calls < 3) throw new InvalidOperationException("vorübergehend");
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.Equal(3, calls);
        Assert.Empty(ReadDlt(topic, TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task Gives_up_after_three_attempts_and_writes_dlt_with_headers()
    {
        var (topic, result) = await Setup();
        using var producer = Producer();
        var calls = 0;

        await new DeadLetter(producer, Group, NullLogger.Instance).HandleAsync(result, () =>
        {
            calls++;
            throw new InvalidOperationException("kaputt");
        }, CancellationToken.None);

        Assert.Equal(3, calls);
        var dlt = Assert.Single(ReadDlt(topic, TimeSpan.FromSeconds(10), expected: 1));
        Assert.Equal("k", dlt.Key);
        Assert.Equal("v", dlt.Value);
        Assert.Contains("kaputt", Header(dlt, "dlt-error"));
        Assert.Equal(topic, Header(dlt, "dlt-original-topic"));
        Assert.Equal("0", Header(dlt, "dlt-original-partition"));
        Assert.Equal(result.Offset.Value.ToString(), Header(dlt, "dlt-original-offset"));
        Assert.Equal("3", Header(dlt, "dlt-attempts"));
        Assert.NotNull(Header(dlt, "traceparent"));
    }

    private sealed class TransientDbError() : System.Data.Common.DbException("DB kurz weg")
    {
        public override bool IsTransient => true;
    }

    [Fact]
    public async Task Transient_db_error_is_rethrown_instead_of_dlt()
    {
        var (topic, result) = await Setup();
        using var producer = Producer();
        var calls = 0;

        await Assert.ThrowsAsync<TransientDbError>(() => new DeadLetter(producer, Group, NullLogger.Instance).HandleAsync(result, () =>
        {
            calls++;
            throw new TransientDbError();
        }, CancellationToken.None));

        Assert.Equal(3, calls);
        Assert.Empty(ReadDlt(topic, TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task Send_writes_poison_without_retry()
    {
        var (topic, result) = await Setup();
        using var producer = Producer();

        await new DeadLetter(producer, Group, NullLogger.Instance).SendAsync(result, new JsonException(), 1, CancellationToken.None);

        var dlt = Assert.Single(ReadDlt(topic, TimeSpan.FromSeconds(10), expected: 1));
        Assert.Equal("1", Header(dlt, "dlt-attempts"));
    }

    private async Task<(string Topic, ConsumeResult<string, string> Result)> Setup()
    {
        var topic = $"t-{Guid.NewGuid():N}";
        await Topics.EnsureTopicAsync(kafka.BootstrapServers, topic);
        await Topics.EnsureTopicAsync(kafka.BootstrapServers, DeadLetter.TopicFor(topic, Group));
        var result = new ConsumeResult<string, string>
        {
            Topic = topic,
            Partition = 0,
            Offset = 42,
            Message = new Message<string, string>
            {
                Key = "k",
                Value = "v",
                Headers = new Headers { { "traceparent", "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"u8.ToArray() } },
            },
        };
        return (topic, result);
    }

    private List<Message<string, string>> ReadDlt(string topic, TimeSpan wait, int expected = int.MaxValue)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"dlt-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(DeadLetter.TopicFor(topic, Group));
        var messages = new List<Message<string, string>>();
        var deadline = DateTime.UtcNow + wait;
        while (messages.Count < expected && DateTime.UtcNow < deadline)
            if (consumer.Consume(TimeSpan.FromMilliseconds(200)) is { } r) messages.Add(r.Message);
        consumer.Close();
        return messages;
    }

    private static string? Header(Message<string, string> m, string key) =>
        m.Headers.TryGetLastBytes(key, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;

    private IProducer<string, string> Producer() =>
        new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build();
}
