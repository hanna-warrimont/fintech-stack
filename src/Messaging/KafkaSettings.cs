using Confluent.Kafka;
using Microsoft.Extensions.Configuration;

namespace Portal.Messaging;

/// <summary>
/// Kafka-Client-Konfiguration für alle Dienste – und die EINE Stelle, an der sich lokales Kafka und Azure Event Hubs
/// im Client-Code unterscheiden (Landkarte 25, docs/kafka-vs-event-hubs.md):
/// <list type="bullet">
/// <item>Ist <c>Kafka:SaslPassword</c> gesetzt (Event-Hubs-Connection-String), läuft die Verbindung über SASL_SSL/PLAIN
/// mit Benutzer <c>$ConnectionString</c>, dazu die von Microsoft empfohlenen Werte gegen Leerlauf-Abbrüche.</item>
/// <item><c>Kafka:ManageTopics=false</c>: Event Hubs erlaubt keine Topic-Verwaltung per AdminClient – die Topics kommen
/// aus Bicep (infra/modules/eventhubs.bicep).</item>
/// </list>
/// </summary>
public sealed class KafkaSettings(IConfiguration config)
{
    public string BootstrapServers { get; } = config.GetConnectionString("kafka")
        ?? throw new InvalidOperationException("ConnectionStrings:kafka fehlt.");

    private string? SaslPassword => config["Kafka:SaslPassword"];

    private bool ManageTopics => !bool.TryParse(config["Kafka:ManageTopics"], out var manage) || manage; // Standard: ja

    /// <summary>
    /// Idempotenter Producer (acks=all): Der Broker verwirft Duplikate, die durch Retries INNERHALB dieses Producers
    /// entstehen. Gegen Duplikate durch einen Neustart (z. B. des Outbox-Dispatchers) hilft das nicht – dafür haben die
    /// Consumer ihre Inbox. Auch Event Hubs unterstützt den idempotenten Producer.
    /// </summary>
    public ProducerConfig Producer()
    {
        var producer = Apply(new ProducerConfig { Acks = Acks.All, EnableIdempotence = true });
        if (SaslPassword is not null) producer.RequestTimeoutMs = 60000; // Event Hubs: > 20 s, librdkafka-Standard 5 s ist zu knapp
        return producer;
    }

    /// <summary>Manueller Commit ist überall Pflicht – die Dienste committen selbst (oder bewusst gar nicht).</summary>
    public ConsumerConfig Consumer(string groupId, AutoOffsetReset reset)
    {
        var consumer = Apply(new ConsumerConfig { GroupId = groupId, AutoOffsetReset = reset, EnableAutoCommit = false });
        if (SaslPassword is not null) consumer.SessionTimeoutMs = 30000;
        return consumer;
    }

    public Task EnsureTopicAsync(string topic, IReadOnlyDictionary<string, string>? topicConfig = null) =>
        ManageTopics ? Topics.EnsureTopicAsync(BootstrapServers, topic, config: topicConfig) : Task.CompletedTask;

    private T Apply<T>(T client) where T : ClientConfig
    {
        client.BootstrapServers = BootstrapServers;
        if (SaslPassword is null) return client;

        client.SecurityProtocol = SecurityProtocol.SaslSsl;
        client.SaslMechanism = SaslMechanism.Plain;
        client.SaslUsername = "$ConnectionString";
        client.SaslPassword = SaslPassword;
        client.SocketKeepaliveEnable = true;  // Azure schließt Verbindungen nach 240 s Leerlauf
        client.MetadataMaxAgeMs = 180000;     // muss < 240 s sein
        return client;
    }
}
