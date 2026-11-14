using Confluent.Kafka;
using Confluent.Kafka.Admin;

namespace Portal.Messaging;

public static class Topics
{
    public const string Ticks = "ticks.v1";
    public const string Instruments = "instruments.v1";
    public const string CorporateActions = "corporate-actions.v1";

    /// <summary>
    /// Kompaktiert: Kafka behält pro Key mindestens die letzte Nachricht. Für Snapshots (State Transfer) genau richtig –
    /// wer von vorn liest, bekommt den aktuellen Stand jedes Instruments, ohne die ganze Historie.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Compacted =
        new Dictionary<string, string> { ["cleanup.policy"] = "compact" };

    /// <summary>
    /// Fakten (keine Zustände) ohne Ablaufdatum: History braucht jeden wirksamen Split, auch Jahre später
    /// beim Neuaufbau. Löschen nach Zeit würde Anpassungsfaktoren verlieren, Kompaktieren alle bis auf den letzten pro Key.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> LongRetention =
        new Dictionary<string, string> { ["cleanup.policy"] = "delete", ["retention.ms"] = "-1" };

    /// <summary>Legt das Topic an, falls es fehlt. Auto-Create würde nur 1 Partition und Standard-Config anlegen.</summary>
    public static async Task EnsureTopicAsync(
        string bootstrapServers, string topic, int partitions = 6, IReadOnlyDictionary<string, string>? config = null)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();
        try
        {
            await admin.CreateTopicsAsync([new TopicSpecification
            {
                Name = topic,
                NumPartitions = partitions,
                ReplicationFactor = 1,
                Configs = config?.ToDictionary(),
            }]);
        }
        catch (CreateTopicsException e) when (e.Results.All(r => r.Error.Code is ErrorCode.NoError or ErrorCode.TopicAlreadyExists))
        {
        }
    }
}
