using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Portal.Messaging;

namespace Portal.Tests.Messaging;

[Collection("infra")]
public sealed class RetentionJobTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Deletes_only_processed_rows_older_than_retention()
    {
        await using var db = NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync());
        await using (var connection = await db.OpenConnectionAsync())
            await connection.ExecuteAsync("""
                CREATE TABLE outbox_messages (id int PRIMARY KEY, processed_at timestamptz);
                INSERT INTO outbox_messages VALUES (1, @old), (2, @recent), (3, NULL);
                """, new { old = Now.AddDays(-8), recent = Now.AddDays(-6) });
        var job = new RetentionJob(db, RetentionJob.Outbox, TimeSpan.FromDays(7), new FixedTime(Now), NullLogger<RetentionJob>.Instance);

        Assert.Equal(1, await job.RunOnceAsync(CancellationToken.None));

        await using var check = await db.OpenConnectionAsync();
        Assert.Equal([2, 3], await check.QueryAsync<int>("SELECT id FROM outbox_messages ORDER BY id")); // offene Zeile (3) bleibt
    }
}

// Ohne Collection "infra": braucht keine Datenbank und läuft auch ohne Docker.
public sealed class RetentionJobGuardTests
{
    [Fact]
    public async Task Rejects_unknown_table() =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new RetentionJob(null!, "instruments; DROP TABLE x", TimeSpan.FromDays(1), TimeProvider.System, NullLogger<RetentionJob>.Instance)
                .RunOnceAsync(CancellationToken.None));
}
