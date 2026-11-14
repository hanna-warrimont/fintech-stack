using Dapper;
using Npgsql;
using Portal.History;
using Portal.Messaging.Contracts;

namespace Portal.Tests.History;

[Collection("infra")]
public sealed class CandleStoreTests(PostgresFixture postgres)
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 11, 14, 10, 0, 30, TimeSpan.Zero);

    // Dapper liefert timestamptz als DateTime (UTC)
    private sealed record Candle(DateTime Bucket, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

    [Fact]
    public void CandleBucket_truncates_to_minute_in_utc() =>
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero),
            CandleBucket.Of(new DateTimeOffset(2026, 11, 14, 11, 0, 59, 900, TimeSpan.FromHours(1))));

    [Fact]
    public async Task First_tick_creates_candle()
    {
        var (store, db) = await NewStore();

        Assert.True(await store.ApplyAsync(new Tick(A, 1, 100.00m, 5, T0), default));

        var c = Assert.Single(await Candles(db));
        Assert.Equal(new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc), c.Bucket);
        Assert.Equal(new Candle(c.Bucket, 100, 100, 100, 100, 5), c);
    }

    [Fact]
    public async Task Ticks_in_same_minute_update_high_low_close_volume()
    {
        var (store, db) = await NewStore();

        await store.ApplyAsync(new Tick(A, 1, 100m, 5, T0), default);
        await store.ApplyAsync(new Tick(A, 2, 105m, 7, T0.AddSeconds(10)), default);
        await store.ApplyAsync(new Tick(A, 3, 98m, 11, T0.AddSeconds(20)), default);

        var c = Assert.Single(await Candles(db));
        Assert.Equal(new Candle(c.Bucket, 100, 105, 98, 98, 23), c);
    }

    [Fact]
    public async Task Duplicate_seq_is_ignored()
    {
        var (store, db) = await NewStore();
        var tick = new Tick(A, 1, 100m, 5, T0);

        Assert.True(await store.ApplyAsync(tick, default));
        Assert.False(await store.ApplyAsync(tick, default));

        Assert.Equal(5, Assert.Single(await Candles(db)).Volume);
    }

    [Fact]
    public async Task Older_seq_is_ignored()
    {
        var (store, _) = await NewStore();

        await store.ApplyAsync(new Tick(A, 5, 100m, 5, T0), default);

        Assert.False(await store.ApplyAsync(new Tick(A, 4, 100m, 5, T0), default));
    }

    [Fact]
    public async Task Late_tick_updates_its_own_minute()
    {
        var (store, db) = await NewStore();
        var ten = new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

        await store.ApplyAsync(new Tick(A, 1, 110m, 1, ten.AddMinutes(5).AddSeconds(10)), default);
        Assert.True(await store.ApplyAsync(new Tick(A, 2, 90m, 1, ten.AddSeconds(10)), default)); // verspätet

        var candles = await Candles(db);
        Assert.Equal(2, candles.Count);
        Assert.Equal(90, candles[0].Close);  // 10:00
        Assert.Equal(110, candles[1].Close); // 10:05
    }

    [Fact]
    public async Task Earlier_event_time_with_higher_seq_becomes_open()
    {
        var (store, db) = await NewStore();

        await store.ApplyAsync(new Tick(A, 1, 100m, 1, T0.AddSeconds(20)), default);
        await store.ApplyAsync(new Tick(A, 2, 95m, 1, T0.AddSeconds(5)), default); // verspätet, gleiche Minute

        var c = Assert.Single(await Candles(db));
        Assert.Equal(95, c.Open);
        Assert.Equal(100, c.Close);
    }

    [Fact]
    public async Task Clear_empties_both_tables()
    {
        var (store, db) = await NewStore();
        await store.ApplyAsync(new Tick(A, 1, 100m, 5, T0), default);
        Assert.False(await store.IsEmptyAsync(default));

        await store.ClearAsync(default);

        Assert.True(await store.IsEmptyAsync(default));
        await using var connection = await db.OpenConnectionAsync();
        Assert.Equal(0, await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM instrument_seq"));
    }

    private async Task<(CandleStore Store, NpgsqlDataSource Db)> NewStore()
    {
        var db = NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync());
        await HistorySchema.EnsureAsync(db, default);
        return (new CandleStore(db), db);
    }

    private static async Task<List<Candle>> Candles(NpgsqlDataSource db)
    {
        await using var connection = await db.OpenConnectionAsync();
        return (await connection.QueryAsync<Candle>(
            "SELECT bucket, open, high, low, close, volume FROM candles_1m ORDER BY bucket")).ToList();
    }
}
