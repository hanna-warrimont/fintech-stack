using Dapper;
using Npgsql;
using Portal.Messaging.Contracts;

namespace Portal.History;

/// <summary>
/// Verdichtet Ticks zu 1-Minuten-Kerzen. Zwei Zeiten, zwei Aufgaben:
/// <list type="bullet">
/// <item><c>seq</c> ist die Transport-Reihenfolge pro Instrument → Deduplizierung (Duplikat oder veraltet = verwerfen).</item>
/// <item><c>EventTime</c> ist die fachliche Zeit → bestimmt die Kerze. Ein verspäteter Tick hat eine neue <c>seq</c>,
/// aber eine alte <c>EventTime</c>: Er wird nicht verworfen, sondern landet in seiner alten Minute.</item>
/// </list>
/// Dedupe und Kerze laufen in EINER Transaktion – sonst könnte ein Absturz dazwischen einen Tick als „gesehen“
/// markieren, ohne ihn zu zählen.
/// </summary>
public sealed class CandleStore(NpgsqlDataSource db)
{
    /// <returns><c>false</c> bei Duplikat oder veralteter <c>seq</c>.</returns>
    public async Task<bool> ApplyAsync(Tick tick, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var fresh = await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO instrument_seq (instrument_id, last_seq) VALUES (@InstrumentId, @Seq)
            ON CONFLICT (instrument_id) DO UPDATE SET last_seq = excluded.last_seq
            WHERE instrument_seq.last_seq < excluded.last_seq
            """, new { tick.InstrumentId, tick.Seq }, tx, cancellationToken: ct));
        if (fresh == 0) return false; // Rollback durch Dispose

        // open/close richten sich nach der Event-Time innerhalb der Minute, nicht nach der Ankunftsreihenfolge.
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO candles_1m (instrument_id, bucket, open, high, low, close, volume, open_time, close_time)
            VALUES (@InstrumentId, @Bucket, @Price, @Price, @Price, @Price, @Volume, @EventTime, @EventTime)
            ON CONFLICT (instrument_id, bucket) DO UPDATE SET
              open  = CASE WHEN excluded.open_time  <  candles_1m.open_time  THEN excluded.open  ELSE candles_1m.open  END,
              open_time  = LEAST(candles_1m.open_time, excluded.open_time),
              close = CASE WHEN excluded.close_time >= candles_1m.close_time THEN excluded.close ELSE candles_1m.close END,
              close_time = GREATEST(candles_1m.close_time, excluded.close_time),
              high = GREATEST(candles_1m.high, excluded.high),
              low  = LEAST(candles_1m.low, excluded.low),
              volume = candles_1m.volume + excluded.volume
            """, new { tick.InstrumentId, Bucket = CandleBucket.Of(tick.EventTime), tick.Price, tick.Volume, tick.EventTime },
            tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> IsEmptyAsync(CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        return !await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM candles_1m)", cancellationToken: ct));
    }

    public async Task ClearAsync(CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "TRUNCATE candles_1m, instrument_seq", cancellationToken: ct));
    }
}
