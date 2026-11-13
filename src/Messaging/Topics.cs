using Confluent.Kafka;
using Confluent.Kafka.Admin;

namespace Portal.Messaging;

public static class Topics
{
    public const string Ticks = "ticks.v1";

    /// <summary>Legt das Topic an, falls es fehlt. Auto-Create würde nur 1 Partition anlegen.</summary>
    public static async Task EnsureTopicAsync(string bootstrapServers, string topic, int partitions = 6)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();
        try
        {
            await admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = partitions, ReplicationFactor = 1 }]);
        }
        catch (CreateTopicsException e) when (e.Results.All(r => r.Error.Code is ErrorCode.NoError or ErrorCode.TopicAlreadyExists))
        {
        }
    }
}
