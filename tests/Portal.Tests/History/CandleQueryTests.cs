using Npgsql;
using Portal.History;
using Portal.Messaging.Contracts;

namespace Portal.Tests.History;

[Collection("infra")]
public sealed class CandleQueryTests(PostgresFixture postgres)
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly DateTimeOffset Ten = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task One_minute_returns_candles_ascending()
    {
        var (store, query) = await Setup(Ten.AddMinutes(30));
        long seq = 0;
        foreach (var minute in new[] { 2, 0, 1 })
            await store.ApplyAsync(new Tick(A, ++seq, 100m, 1, Ten.AddMinutes(minute).AddSeconds(5)), default);

        var candles = await query.Handle(A, "1m", default);

        Assert.Equal([0, 60, 120], candles.Select(c => c.Time - Ten.ToUnixTimeSeconds()));
    }

    [Fact]
    public async Task One_hour_rolls_up_minutes()
    {
        var (store, query) = await Setup(Ten.AddHours(1).AddMinutes(30));
        await store.ApplyAsync(new Tick(A, 1, 100m, 1, Ten.AddSeconds(10)), default);
        await store.ApplyAsync(new Tick(A, 2, 101m, 2, Ten.AddSeconds(20)), default);
        await store.ApplyAsync(new Tick(A, 3, 90m, 3, Ten.AddMinutes(30)), default);
        await store.ApplyAsync(new Tick(A, 4, 95m, 4, Ten.AddMinutes(59)), default);

        var candle = Assert.Single(await query.Handle(A, "1h", default));

        Assert.Equal(new CandleDto(Ten.ToUnixTimeSeconds(), 100, 101, 90, 95, 10), candle);
    }

    [Fact]
    public async Task Window_excludes_old_candles()
    {
        var (store, query) = await Setup(Ten.AddHours(3));
        await store.ApplyAsync(new Tick(A, 1, 100m, 1, Ten), default);
        await store.ApplyAsync(new Tick(A, 2, 100m, 1, Ten.AddHours(2).AddMinutes(30)), default);

        var candle = Assert.Single(await query.Handle(A, "1m", default));

        Assert.Equal(Ten.AddHours(2).AddMinutes(30).ToUnixTimeSeconds(), candle.Time);
    }

    [Fact]
    public async Task Empty_history_returns_empty_list()
    {
        var (_, query) = await Setup(Ten);

        Assert.Empty(await query.Handle(A, "1d", default));
    }

    [Fact]
    public void Unknown_interval_is_rejected()
    {
        Assert.False(CandleQuery.IsValidInterval("5m"));
        Assert.True(CandleQuery.IsValidInterval("1h"));
    }

    private async Task<(CandleStore Store, CandleQuery Query)> Setup(DateTimeOffset now)
    {
        var db = NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync());
        await HistorySchema.EnsureAsync(db, default);
        return (new CandleStore(db), new CandleQuery(db, new FixedTime(now)));
    }
}
