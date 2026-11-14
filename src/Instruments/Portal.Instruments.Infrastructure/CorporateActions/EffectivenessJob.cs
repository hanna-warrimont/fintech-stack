using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Portal.Instruments.Application.Abstractions;
using Portal.Instruments.Domain.CorporateActions;

namespace Portal.Instruments.Infrastructure.CorporateActions;

/// <summary>
/// Zeitgesteuerte Wirksamkeit: Freigegebene Maßnahmen werden zum Stichtag wirksam – Instrument ändern, Status setzen und
/// <c>BecameEffective</c> (plus ggf. Snapshot) in die Outbox schreiben, alles in EINER Transaktion pro Maßnahme.
/// Sicher bei mehreren Instanzen und Neustarts:
/// <list type="bullet">
/// <item><c>FOR UPDATE SKIP LOCKED</c>: Zwei Jobs fassen nie dieselbe Zeile an – der zweite überspringt, was der erste hält.</item>
/// <item>Zustandsübergang als Idempotenz: Eine wirksame Maßnahme ist nicht mehr <c>Approved</c> und wird nie wieder gewählt.
/// Stirbt der Job vor dem Commit, bleibt alles beim Alten und der nächste Lauf holt es nach.</item>
/// <item>Eine Transaktion pro Maßnahme: Eine nicht anwendbare Maßnahme wird storniert und blockiert keine anderen.</item>
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
        var now = time.GetUtcNow();
        var seen = new List<Guid>();
        var applied = 0;
        for (var i = 0; i < BatchSize; i++)
        {
            var (id, effective) = await ApplyNextAsync(now, seen, ct);
            if (id is null) break;
            seen.Add(id.Value);
            if (effective) applied++;
        }
        return applied;
    }

    private async Task<(Guid? Id, bool Effective)> ApplyNextAsync(DateTimeOffset now, List<Guid> seen, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InstrumentDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Erst die Zeile sperren (Id), dann das Aggregat laden – die Sperre gilt bis zum Commit.
        // (FromSql auf die Entität scheitert an den Spalten der Complex Property SplitRatio.)
        var id = (await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM corporate_actions
            WHERE status = 'Approved' AND effective_at <= {now} AND id <> ALL({seen.ToArray()})
            ORDER BY effective_at LIMIT 1
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(ct)).SingleOrDefault();
        if (id == Guid.Empty) return (null, false);

        // Eigener Span je Maßnahme: Seine Id landet als traceparent in der Outbox – Trace 2 (Job → BecameEffective → History).
        using var activity = Source.StartActivity("corporate-action.effective");
        activity?.SetTag("corporateActionId", id);

        var action = await db.CorporateActions.FirstAsync(a => a.Id == id, ct);
        var instrument = await db.Instruments.FirstAsync(i => i.Id == action.InstrumentId, ct);
        var result = action.BecomeEffective(instrument, now);
        if (result.IsFailure)
        {
            log.LogWarning("Maßnahme {CorporateActionId} nicht anwendbar ({Error}), wird storniert", id, result.Error.Code);
            action.CancelAsInapplicable();
        }

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return (id, result.IsSuccess);
        }
        catch (DuplicateKeyException)
        {
            // Neue ISIN ist inzwischen vergeben: Diese Transaktion ist verloren, stornieren in einer neuen.
            log.LogWarning("Maßnahme {CorporateActionId}: neue ISIN bereits vergeben, wird storniert", id);
        }

        await transaction.RollbackAsync(ct);
        await CancelInNewScopeAsync(id, ct);
        return (id, false);
    }

    private async Task CancelInNewScopeAsync(Guid id, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InstrumentDbContext>();
        var action = await db.CorporateActions.FirstAsync(a => a.Id == id, ct);
        if (action.CancelAsInapplicable().IsSuccess) await db.SaveChangesAsync(ct);
    }
}
