using System.Diagnostics;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Portal.Messaging;

namespace Portal.Tests.Messaging;

public sealed class KafkaSettingsTests
{
    private static KafkaSettings Settings(params (string Key, string Value)[] values) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build());

    [Fact]
    public void Plain_kafka_without_password()
    {
        var settings = Settings(("ConnectionStrings:kafka", "localhost:9092"));

        Assert.Equal("localhost:9092", settings.BootstrapServers);
        Assert.Null(settings.Producer().SecurityProtocol);
        Assert.Null(settings.Consumer("g", AutoOffsetReset.Earliest).SecurityProtocol);
    }

    [Fact]
    public void Sasl_is_enabled_when_password_is_configured()
    {
        var settings = Settings(("ConnectionStrings:kafka", "ns.servicebus.windows.net:9093"), ("Kafka:SaslPassword", "Endpoint=sb://ns/;SharedAccessKey=x"));

        var producer = settings.Producer();
        var consumer = settings.Consumer("search", AutoOffsetReset.Earliest);

        foreach (ClientConfig c in new ClientConfig[] { producer, consumer })
        {
            Assert.Equal(SecurityProtocol.SaslSsl, c.SecurityProtocol);
            Assert.Equal(SaslMechanism.Plain, c.SaslMechanism);
            Assert.Equal("$ConnectionString", c.SaslUsername);
            Assert.Equal("Endpoint=sb://ns/;SharedAccessKey=x", c.SaslPassword);
            Assert.True(c.SocketKeepaliveEnable);
            Assert.Equal(180000, c.MetadataMaxAgeMs);
        }
        Assert.Equal(60000, producer.RequestTimeoutMs);
        Assert.Equal(30000, consumer.SessionTimeoutMs);
        Assert.Equal(("search", AutoOffsetReset.Earliest, false), (consumer.GroupId, consumer.AutoOffsetReset, consumer.EnableAutoCommit));
    }

    [Fact]
    public void Producer_is_idempotent_with_acks_all()
    {
        var producer = Settings(("ConnectionStrings:kafka", "localhost:9092")).Producer();

        Assert.Equal((Acks.All, true), (producer.Acks, producer.EnableIdempotence));
    }

    [Fact]
    public async Task EnsureTopic_is_skipped_when_topics_are_managed_outside()
    {
        var settings = Settings(("ConnectionStrings:kafka", "localhost:1"), ("Kafka:ManageTopics", "false"));
        var watch = Stopwatch.StartNew();

        await settings.EnsureTopicAsync(Topics.Ticks);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task EnsureTopic_is_skipped_with_sasl_even_without_manage_topics_setting()
    {
        // Event Hubs (SASL) erlaubt keine Topic-Verwaltung – auch wenn Kafka:ManageTopics vergessen wurde.
        var settings = Settings(("ConnectionStrings:kafka", "localhost:1"), ("Kafka:SaslPassword", "secret"));
        var watch = Stopwatch.StartNew();

        await settings.EnsureTopicAsync(Topics.Ticks);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1));
    }
}
