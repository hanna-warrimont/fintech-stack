using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace Portal.Messaging;

/// <summary>
/// Retry und Dead-Letter-Topic für einen Consumer.
/// Vorübergehende Fehler (DB kurz weg, Deadlock) heilen oft von selbst → im Prozess wiederholen, mit Pause.
/// Was nach 3 Versuchen noch scheitert, geht ins DLT <c>&lt;topic&gt;.&lt;group&gt;.dlt</c> – die Partition läuft weiter,
/// die Nachricht ist nicht verloren, sondern liegt mit Fehler-Headern zur Analyse oder zum erneuten Einspielen bereit.
/// Poison (nicht lesbar) geht über <see cref="SendAsync"/> sofort ins DLT: Am Inhalt ändert ein Retry nichts.
/// </summary>
public sealed class DeadLetter(IProducer<string, string> producer, string group, ILogger log)
{
    // ponytail: feste Werte; konfigurierbar, falls verschiedene Consumer verschiedene Geduld brauchen
    private static readonly TimeSpan[] Backoff = [TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1)];

    public static string TopicFor(string topic, string group) => $"{topic}.{group}.dlt";

    /// <summary>Führt <paramref name="process"/> mit bis zu 3 Versuchen aus; danach DLT. Wirft nur bei Abbruch.</summary>
    public async Task HandleAsync(ConsumeResult<string, string> result, Func<Task> process, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await process();
                return;
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                if (attempt > Backoff.Length)
                {
                    await SendAsync(result, e, attempt, ct);
                    return;
                }

                log.LogWarning(e, "Verarbeitung von {TopicPartitionOffset} fehlgeschlagen (Versuch {Attempt}), wiederhole",
                    result.TopicPartitionOffset, attempt);
                await Task.Delay(Backoff[attempt - 1], ct);
            }
        }
    }

    /// <summary>Schreibt die Original-Nachricht (Key, Value, Header) plus Fehler-Header ins DLT und wartet auf die Bestätigung.</summary>
    public async Task SendAsync(ConsumeResult<string, string> result, Exception error, int attempts, CancellationToken ct)
    {
        var headers = new Headers();
        foreach (var h in result.Message.Headers ?? []) headers.Add(h.Key, h.GetValueBytes());
        headers.Add("dlt-error", Encoding.UTF8.GetBytes($"{error.GetType().Name}: {error.Message}"));
        headers.Add("dlt-original-topic", Encoding.UTF8.GetBytes(result.Topic));
        headers.Add("dlt-original-partition", Encoding.UTF8.GetBytes(result.Partition.Value.ToString()));
        headers.Add("dlt-original-offset", Encoding.UTF8.GetBytes(result.Offset.Value.ToString()));
        headers.Add("dlt-attempts", Encoding.UTF8.GetBytes(attempts.ToString()));

        var dlt = TopicFor(result.Topic, group);
        // Erst nach der Bestätigung darf der Aufrufer committen – sonst wäre die Nachricht weg.
        await producer.ProduceAsync(dlt, new Message<string, string> { Key = result.Message.Key, Value = result.Message.Value, Headers = headers }, ct);
        log.LogError(error, "{TopicPartitionOffset} nach {Attempts} Versuch(en) ins DLT {Dlt} geschrieben",
            result.TopicPartitionOffset, attempts, dlt);
    }
}
