using Confluent.Kafka;
using Portal.Messaging;
using Portal.Messaging.Contracts;

namespace Portal.History;

/// <summary>
/// Liest <c>corporate-actions.v1</c> in die Anpassungsfaktoren. Eigene Group (<c>history-corporate-actions</c>), damit
/// Kerzen- und Maßnahmen-Strom sich nicht gegenseitig rebalancen. Commit erst nach Speichern oder DLT (at-least-once,
/// die Inbox im <see cref="AdjustmentStore"/> fängt Doppeltes ab). Ist History aus, holt es die Faktoren beim Start nach.
/// </summary>
public sealed class CorporateActionConsumer(
    AdjustmentStore store, DeadLetter deadLetter, ConsumerConfig config, ILogger<CorporateActionConsumer> log,
    string topic = Topics.CorporateActions)
    : BackgroundService
{
    public const string Group = "history-corporate-actions";

    protected override Task ExecuteAsync(CancellationToken ct) => Task.Run(() => RunAsync(ct), ct);

    private async Task RunAsync(CancellationToken ct)
    {
        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(topic);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<string, string> result;
                try
                {
                    result = consumer.Consume(ct);
                }
                catch (ConsumeException e) when (!e.Error.IsFatal)
                {
                    log.LogWarning(e, "Konsumieren von {Topic} fehlgeschlagen, versuche weiter", topic);
                    continue;
                }

                using var activity = KafkaTracing.StartConsume(topic, result.Message.Headers);
                try
                {
                    if (CorporateActionEvent.TryParse(result.Message.Value) is { } message)
                    {
                        activity?.SetTag("corporateActionId", message.Payload.CorporateActionId);
                        await deadLetter.HandleAsync(result, () => store.ApplyAsync(message, ct), ct);
                    }
                    else
                        await deadLetter.SendAsync(result, new FormatException("Keine gültige Kapitalmaßnahme"), 1, ct);
                    consumer.Commit(result);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.LogError(e, "Maßnahme weder gespeichert noch im DLT, neuer Versuch: {TopicPartitionOffset}", result.TopicPartitionOffset);
                    consumer.Seek(result.TopicPartitionOffset);
                    await Task.Delay(1000, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            consumer.Close();
        }
    }
}
