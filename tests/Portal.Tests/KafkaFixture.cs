using Testcontainers.Kafka;

namespace Portal.Tests;

/// <summary>Ein echter Kafka-Container, geteilt von allen Tests der Collection "kafka".</summary>
public sealed class KafkaFixture : IAsyncLifetime
{
    private readonly KafkaContainer _kafka = new KafkaBuilder("confluentinc/cp-kafka:7.9.0").Build();

    public string BootstrapServers => _kafka.GetBootstrapAddress();

    public Task InitializeAsync() => _kafka.StartAsync();

    public Task DisposeAsync() => _kafka.DisposeAsync().AsTask();
}

[CollectionDefinition("kafka")]
public sealed class KafkaCollection : ICollectionFixture<KafkaFixture>;
