using System.Data.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Portal.Messaging;

/// <summary>
/// Räumt Outbox und Inbox auf: Zeilen, deren Zeitstempel älter als <paramref name="keep"/> ist, werden gelöscht – sonst
/// wachsen beide Tabellen mit jeder Nachricht für immer.
/// <list type="bullet">
/// <item>Outbox (<c>outbox_messages.processed_at</c>): Gesendetes braucht niemand mehr; offene Zeilen haben <c>NULL</c> und
/// bleiben unangetastet.</item>
/// <item>Inbox (<c>processed_messages.processed_at</c>): Kommt eine Nachricht NACH dem Löschen ihrer Inbox-Zeile noch einmal
/// (z. B. Offsets zurückgesetzt), greift der zweite Schutz – <c>revision</c> in Search, der Primärschlüssel der Maßnahme in
/// History. Darum ist das Löschen dort sicher.</item>
/// </list>
/// Mehrere Instanzen stören sich nicht: DELETE ist idempotent.
/// </summary>
// ponytail: ein DELETE ohne Batches und ohne Index auf processed_at; bei Millionen Zeilen in Portionen löschen
public sealed class RetentionJob(
    DbDataSource db, string table, TimeSpan keep, TimeProvider time, ILogger<RetentionJob> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    public const string Outbox = "outbox_messages";
    public const string Inbox = "processed_messages";

    // Tabellenname nur aus den Konstanten oben, nie aus einer Eingabe – geprüft schon beim Erzeugen.
    private readonly string _table = table is Outbox or Inbox ? table : throw new ArgumentException($"Unbekannte Tabelle {table}", nameof(table));

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                var deleted = await RunOnceAsync(ct);
                if (deleted > 0) log.LogInformation("{Table}: {Deleted} Zeilen älter als {Keep} gelöscht", _table, deleted, keep);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "Aufräumen von {Table} fehlgeschlagen, nächster Versuch in {Interval}", _table, Interval);
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    /// <returns>Anzahl gelöschter Zeilen.</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var command = db.CreateCommand($"DELETE FROM {_table} WHERE processed_at < $1");
        var cutoff = command.CreateParameter();
        cutoff.Value = time.GetUtcNow() - keep;
        command.Parameters.Add(cutoff);
        return await command.ExecuteNonQueryAsync(ct);
    }
}
