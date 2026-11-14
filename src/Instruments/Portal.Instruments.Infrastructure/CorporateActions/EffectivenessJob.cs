using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Portal.Instruments.Domain.CorporateActions;

namespace Portal.Instruments.Infrastructure.CorporateActions;

/// <summary>
/// Zeitgesteuerte Wirksamkeit: Freigegebene Maßnahmen werden zum Stichtag wirksam – Instrument ändern, Status setzen und
/// <c>BecameEffective</c> (plus ggf. Snapshot) in die Outbox schreiben, alles in EINER Transaktion.
/// Sicher bei mehreren Instanzen und Neustarts:
/// <list type="bullet">
/// <item><c>FOR UPDATE SKIP LOCKED</c>: Zwei Jobs fassen nie dieselbe Zeile an – der zweite überspringt, was der erste hält.</item>
/// <item>Zustandsübergang als Idempotenz: Eine wirksame Maßnahme ist nicht mehr <c>Approved</c> und wird nie wieder gewählt.
/// Stirbt der Job vor dem Commit, bleibt alles beim Alten und der nächste Lauf holt es nach.</item>
/// </list>
/// </summary>
public sealed class EffectivenessJob(IServiceScopeFactory scopes, TimeProvider time, ILogger<EffectivenessJob> log) : BackgroundService
{
    private const int BatchSize = 10;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    public static readonly ActivitySource Source = new("Portal.Instruments");

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval, time);
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                while (await RunOnceAsync(ct) == BatchSize) { } // Rückstau abbauen
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "Wirksamkeits-Lauf fehlgeschlagen, nächster Versuch in {Interval}", Interval);
            }
        }
    }

    /// <returns>Anzahl der Maßnahmen, die in diesem Lauf wirksam wurden.</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InstrumentDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var now = time.GetUtcNow();
        // Erst die Zeilen sperren (Ids), dann die Aggregate laden – die Sperren gelten bis zum Commit.
        // (FromSql auf die Entität scheitert an den Spalten der Complex Property SplitRatio.)
        var ids = await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM corporate_actions
            WHERE status = 'Approved' AND effective_at <= {now}
            ORDER BY effective_at LIMIT {BatchSize}
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(ct);
        var due = await db.CorporateActions.Where(a => ids.Contains(a.Id)).OrderBy(a => a.EffectiveAt).ToListAsync(ct);

        var applied = 0;
        foreach (var action in due)
        {
            // Eigener Span je Maßnahme: Seine Id landet als traceparent in der Outbox – Trace 2 (Job → BecameEffective → History).
            using var activity = Source.StartActivity("corporate-action.effective");
            activity?.SetTag("corporateActionId", action.Id);

            var instrument = await db.Instruments.FirstAsync(i => i.Id == action.InstrumentId, ct);
            var result = action.BecomeEffective(instrument, now);
            if (result.IsFailure)
            {
                log.LogWarning("Maßnahme {CorporateActionId} nicht wirksam: {Error}", action.Id, result.Error.Code);
                continue;
            }

            await db.SaveChangesAsync(ct); // je Maßnahme, damit die Outbox-Zeile den traceparent DIESES Spans trägt
            applied++;
        }

        await transaction.CommitAsync(ct);
        return applied;
    }
}
