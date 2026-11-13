using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Portal.Messaging;

namespace Portal.Tests.Messaging;

[Collection("infra")]
public sealed class TopicsTests(KafkaFixture kafka)
{
    [Fact]
    public async Task EnsureTopic_applies_config()
    {
        var topic = $"t-{Guid.NewGuid():N}";
        await Topics.EnsureTopicAsync(kafka.BootstrapServers, topic, config: Topics.Compacted);

        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        var configs = await admin.DescribeConfigsAsync([new ConfigResource { Type = ResourceType.Topic, Name = topic }]);
        Assert.Equal("compact", configs.Single().Entries["cleanup.policy"].Value);
        Assert.Equal(6, admin.GetMetadata(topic, TimeSpan.FromSeconds(10)).Topics.Single().Partitions.Count);
    }

    [Fact]
    public async Task EnsureTopic_is_idempotent()
    {
        var topic = $"t-{Guid.NewGuid():N}";
        await Topics.EnsureTopicAsync(kafka.BootstrapServers, topic, config: Topics.Compacted);
        await Topics.EnsureTopicAsync(kafka.BootstrapServers, topic, config: Topics.Compacted);
    }
}
