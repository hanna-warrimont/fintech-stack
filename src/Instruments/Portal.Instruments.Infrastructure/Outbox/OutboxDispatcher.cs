using System.Diagnostics;
using Confluent.Kafka;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Portal.Messaging;

namespace Portal.Instruments.Infrastructure.Outbox;

/// <summary>
/// Liest unverarbeitete Outbox-Zeilen und schickt sie nach Kafka. Zusage: at-least-once – lieber doppelt als nie.
/// </summary>
public sealed class OutboxDispatcher(
    NpgsqlDataSource db, IProducer<string, string> producer, TimeProvider time, ILogger<OutboxDispatcher> log)
    : BackgroundService
{
    private const int BatchSize = 50;

    /// <summary>
    /// Idempotenter Producer: Der Broker verwirft Duplikate, die durch Retries INNERHALB dieses Producers entstehen.
    /// Gegen Duplikate durch einen Neustart des Dispatchers hilft das nicht – dafür haben die Consumer ihre Inbox.
    /// </summary>
    public static ProducerConfig ProducerConfig(string bootstrapServers) => new()
    {
        BootstrapServers = bootstrapServers,
        Acks = Acks.All,
        EnableIdempotence = true,
    };

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (await DispatchBatchAsync(ct) == 0) await Task.Delay(500, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                log.LogWarning(e, "Outbox-Versand fehlgeschlagen, neuer Versuch in 5 s");
                await Task.Delay(5000, ct);
            }
        }
    }

    /// <returns>Anzahl gesendeter Nachrichten; 0 = nichts zu tun.</returns>
    public async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        // SKIP LOCKED: Zeilen, die ein anderer Dispatcher gerade bearbeitet, werden übersprungen statt doppelt gesendet.
        // ponytail: Mit mehreren Dispatchern ist die Reihenfolge pro Key nicht garantiert – deshalb tragen Snapshots eine Revision.
        var rows = (await connection.QueryAsync<Row>(new CommandDefinition("""
            SELECT id, topic, key, payload::text AS payload, trace_parent AS traceparent
            FROM outbox_messages
            WHERE processed_at IS NULL
            ORDER BY occurred_at
            LIMIT @BatchSize
            FOR UPDATE SKIP LOCKED
            """, new { BatchSize }, transaction, cancellationToken: ct))).ToList();
        if (rows.Count == 0) return 0;

        // Nacheinander senden: hält die Reihenfolge pro Key. Wirft ein Send, wird zurückgerollt –
        // schon gesendete Nachrichten dieses Batches gehen beim nächsten Versuch erneut raus (at-least-once).
        foreach (var row in rows)
        {
            var headers = new Headers();
            var parent = ActivityContext.TryParse(row.TraceParent, null, out var context) ? context : default;
            using var activity = KafkaTracing.StartPublish(row.Topic, headers, parent);
            await producer.ProduceAsync(row.Topic, new Message<string, string> { Key = row.Key, Value = row.Payload, Headers = headers }, ct);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE outbox_messages SET processed_at = @now WHERE id = ANY(@ids)",
            new { now = time.GetUtcNow(), ids = rows.Select(r => r.Id).ToArray() }, transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        return rows.Count;
    }

    private sealed record Row(Guid Id, string Topic, string Key, string Payload, string? TraceParent);
}
