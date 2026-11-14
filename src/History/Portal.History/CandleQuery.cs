using Dapper;
using Npgsql;

namespace Portal.History;

/// <summary>Eine Kerze für den Chart; <see cref="Time"/> = Unix-Sekunden des Bucket-Starts (passt direkt zu lightweight-charts).</summary>
public sealed record CandleDto(long Time, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

/// <summary>
/// Liest Kerzen. 1h und 1d werden nicht gespeichert, sondern beim Lesen aus den Minuten gebildet:
/// eine Wahrheit, keine Synchronisation zwischen Tabellen. Ebenso werden Splits erst beim Lesen eingerechnet
/// (Spec: Faktoren statt Umschreiben der Rohkerzen) – ein später eintreffender Split wirkt sofort rückwirkend.
/// </summary>
// ponytail: Rollup bei jeder Abfrage; bei vielen Lesern eine materialisierte Sicht oder eigene Stunden-Tabelle
public sealed class CandleQuery(NpgsqlDataSource db, TimeProvider time)
{
    private const int MaxCandles = 500;

    // Feste Zuordnung: Die Unit kommt nie aus der Nutzereingabe ins SQL.
    private static readonly Dictionary<string, (string Unit, TimeSpan Window)> Intervals = new()
    {
        ["1m"] = ("minute", TimeSpan.FromHours(2)),
        ["1h"] = ("hour", TimeSpan.FromDays(7)),
        ["1d"] = ("day", TimeSpan.FromDays(365)),
    };

    public static bool IsValidInterval(string interval) => Intervals.ContainsKey(interval);

    public async Task<IReadOnlyList<CandleDto>> Handle(Guid instrumentId, string interval, CancellationToken ct)
    {
        var (unit, window) = Intervals[interval];
        await using var connection = await db.OpenConnectionAsync(ct);
        var candles = await connection.QueryAsync<CandleDto>(new CommandDefinition("""
            WITH adjusted AS (
              -- Split-Anpassung beim Lesen: je Kerze das Produkt aller Faktoren, die NACH ihr wirksam wurden
              -- (ln/exp = Produkt über Zeilen). Preise × Faktor, Volumen ÷ Faktor. Die Rohkerzen bleiben unverändert.
              SELECT c.bucket, c.open * adj.f AS open, c.high * adj.f AS high, c.low * adj.f AS low,
                     c.close * adj.f AS close, c.volume / adj.f AS volume
              FROM candles_1m c
              LEFT JOIN LATERAL (SELECT coalesce(exp(sum(ln(a.factor))), 1) AS f FROM adjustments a
                                 WHERE a.instrument_id = c.instrument_id AND c.bucket < a.valid_until) adj ON true
              WHERE c.instrument_id = @instrumentId AND c.bucket >= @from)
            SELECT extract(epoch FROM date_trunc(@unit, bucket, 'UTC'))::bigint AS time,
                   round((array_agg(open ORDER BY bucket))[1], 4) AS open, round(max(high), 4) AS high,
                   round(min(low), 4) AS low, round((array_agg(close ORDER BY bucket DESC))[1], 4) AS close,
                   round(sum(volume))::bigint AS volume
            FROM adjusted
            GROUP BY 1 ORDER BY 1 LIMIT @limit
            """, new { unit, instrumentId, from = time.GetUtcNow() - window, limit = MaxCandles }, cancellationToken: ct));
        return candles.AsList();
    }
}
