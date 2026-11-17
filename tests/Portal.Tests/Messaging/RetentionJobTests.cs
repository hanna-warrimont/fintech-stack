using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Portal.History;
using Portal.Instruments.Infrastructure;
using Portal.Messaging;
using Portal.Search;

namespace Portal.Tests.Messaging;

/// <summary>Gegen die echten Schemas, auf die der Job in Instrument (Outbox), History und Search (Inbox) losgelassen wird.</summary>
[Collection("infra")]
public sealed class RetentionJobTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Outbox_deletes_only_processed_rows_older_than_retention()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var sp = new ServiceCollection().AddInstrumentInfrastructure(connectionString).BuildServiceProvider())
        await using (var scope = sp.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<InstrumentDbContext>().Database.MigrateAsync();
        await using var db = NpgsqlDataSource.Create(connectionString);
        Guid old = Guid.NewGuid(), recent = Guid.NewGuid(), open = Guid.NewGuid();
        await using (var connection = await db.OpenConnectionAsync())
            await connection.ExecuteAsync("""
                INSERT INTO outbox_messages (id, topic, key, type, payload, occurred_at, processed_at)
                VALUES (@id, 't', 'k', 'T', '{}', @at, @processed)
                """, new[]
            {
                new { id = old, at = Now.AddDays(-9), processed = (DateTimeOffset?)Now.AddDays(-8) },
                new { id = recent, at = Now.AddDays(-9), processed = (DateTimeOffset?)Now.AddDays(-6) },
                new { id = open, at = Now.AddDays(-9), processed = (DateTimeOffset?)null }, // offen: bleibt, egal wie alt
            });

        Assert.Equal(1, await Job(db, RetentionJob.Outbox, TimeSpan.FromDays(7)).RunOnceAsync(CancellationToken.None));

        await using var check = await db.OpenConnectionAsync();
        Assert.Equal(new[] { recent, open }.Order(), (await check.QueryAsync<Guid>("SELECT id FROM outbox_messages")).Order());
    }

    [Theory]
    [InlineData("history")]
    [InlineData("search")]
    public async Task Inbox_deletes_rows_older_than_retention(string service)
    {
        await using var db = NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync());
        await (service == "history" ? HistorySchema.EnsureAsync(db, default) : SearchSchema.EnsureAsync(db, default));
        Guid old = Guid.NewGuid(), recent = Guid.NewGuid();
        await using (var connection = await db.OpenConnectionAsync())
            await connection.ExecuteAsync("INSERT INTO processed_messages (message_id, processed_at) VALUES (@id, @at)",
                new[] { new { id = old, at = Now.AddDays(-31) }, new { id = recent, at = Now.AddDays(-29) } });

        Assert.Equal(1, await Job(db, RetentionJob.Inbox, TimeSpan.FromDays(30)).RunOnceAsync(CancellationToken.None));

        await using var check = await db.OpenConnectionAsync();
        Assert.Equal([recent], await check.QueryAsync<Guid>("SELECT message_id FROM processed_messages"));
    }

    private static RetentionJob Job(NpgsqlDataSource db, string table, TimeSpan keep) =>
        new(db, table, keep, new FixedTime(Now), NullLogger<RetentionJob>.Instance);
}

// Ohne Collection "infra": braucht keine Datenbank und läuft auch ohne Docker.
public sealed class RetentionJobGuardTests
{
    [Fact]
    public void Rejects_unknown_table() =>
        Assert.Throws<ArgumentException>(() =>
            new RetentionJob(null!, "instruments; DROP TABLE x", TimeSpan.FromDays(1), TimeProvider.System, NullLogger<RetentionJob>.Instance));
}
