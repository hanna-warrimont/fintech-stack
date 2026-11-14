using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Portal.History;
using Portal.Messaging;
using Portal.Messaging.Contracts;

namespace Portal.Tests.History;

[Collection("infra")]
public sealed class AdjustmentTests(KafkaFixture kafka, PostgresFixture postgres)
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly DateTimeOffset Ten = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Split_factor_adjusts_older_candles_only()
    {
        var (db, candles, adjustments, query) = await Setup();
        await candles.ApplyAsync(new Tick(A, 1, 100m, 2, Ten.AddSeconds(10)), default);
        await candles.ApplyAsync(new Tick(A, 2, 26m, 3, Ten.AddMinutes(10)), default);

        Assert.True(await adjustments.ApplyAsync(Effective(Split(1, 4), Ten.AddMinutes(5)), default));

        var result = await query.Handle(A, "1m", default);
        Assert.Equal([25m, 26m], result.Select(c => c.Close));
        Assert.Equal([8L, 3L], result.Select(c => c.Volume));
        await using var connection = await db.OpenConnectionAsync();
        Assert.Equal(100m, await connection.ExecuteScalarAsync<decimal>("SELECT close FROM candles_1m ORDER BY bucket LIMIT 1")); // Rohkerze unverändert
    }

    [Fact]
    public async Task Two_splits_multiply()
    {
        var (_, candles, adjustments, query) = await Setup();
        await candles.ApplyAsync(new Tick(A, 1, 100m, 1, Ten.AddSeconds(10)), default);

        await adjustments.ApplyAsync(Effective(Split(1, 2), Ten.AddMinutes(5)), default);
        await adjustments.ApplyAsync(Effective(Split(1, 2), Ten.AddMinutes(6)), default);

        Assert.Equal(25m, Assert.Single(await query.Handle(A, "1m", default)).Close);
    }

    [Fact]
    public async Task Same_message_twice_stores_one_factor()
    {
        var (db, _, adjustments, _) = await Setup();
        var message = Effective(Split(1, 4), Ten);

        Assert.True(await adjustments.ApplyAsync(message, default));
        Assert.False(await adjustments.ApplyAsync(message, default));

        Assert.Equal(1, await Count(db, "adjustments"));
    }

    [Fact]
    public async Task Non_split_events_are_marked_processed_without_factor()
    {
        var (db, _, adjustments, _) = await Setup();

        Assert.True(await adjustments.ApplyAsync(Envelope(CorporateActionEvent.Approved, Split(1, 4)), default));
        Assert.True(await adjustments.ApplyAsync(Effective(Split(1, 4) with { Kind = "Delisting", SplitOld = null, SplitNew = null }, Ten), default));

        Assert.Equal(0, await Count(db, "adjustments"));
        Assert.Equal(2, await Count(db, "processed_messages"));
    }

    [Fact]
    public async Task Consumer_applies_split_from_topic()
    {
        var (db, _, adjustments, _) = await Setup();
        var topic = $"ca-test-{Guid.NewGuid():N}";
        const string group = "history-corporate-actions";
        await Topics.EnsureTopicAsync(kafka.BootstrapServers, topic);
        await Topics.EnsureTopicAsync(kafka.BootstrapServers, DeadLetter.TopicFor(topic, group));
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        var delivered = await producer.ProduceAsync(topic, new Message<string, string>
        {
            Key = A.ToString("D"),
            Value = JsonSerializer.Serialize(Effective(Split(1, 4), Ten), JsonSerializerOptions.Web),
        });
        var groupId = $"{group}-{Guid.NewGuid():N}";
        var consumer = new CorporateActionConsumer(adjustments, new DeadLetter(producer, group, NullLogger.Instance), new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }, NullLogger<CorporateActionConsumer>.Instance, topic);

        await consumer.StartAsync(CancellationToken.None);
        try
        {
            using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafka.BootstrapServers }).Build();
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                var committed = (await admin.ListConsumerGroupOffsetsAsync([new ConsumerGroupTopicPartitions(groupId, [delivered.TopicPartition])]))
                    .Single().Partitions.Single().Offset.Value;
                if (await Count(db, "adjustments") == 1 && committed > delivered.Offset.Value) break;
                await Task.Delay(250);
            }
            Assert.Equal(1, await Count(db, "adjustments"));
            Assert.True(DateTime.UtcNow < deadline, "Offset nicht committet");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    private static CorporateActionEvent Split(int old, int @new) => new(Guid.NewGuid(), A, "Split", old, @new, null, Ten);

    private static Envelope<CorporateActionEvent> Effective(CorporateActionEvent e, DateTimeOffset effectiveAt) =>
        Envelope(CorporateActionEvent.BecameEffective, e with { EffectiveAt = effectiveAt });

    private static Envelope<CorporateActionEvent> Envelope(string type, CorporateActionEvent e) =>
        new(Guid.NewGuid(), type, CorporateActionEvent.MessageVersion, Ten, e);

    private async Task<(NpgsqlDataSource Db, CandleStore Candles, AdjustmentStore Adjustments, CandleQuery Query)> Setup()
    {
        var db = NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync());
        await HistorySchema.EnsureAsync(db, default);
        return (db, new CandleStore(db), new AdjustmentStore(db), new CandleQuery(db, new FixedTime(Ten.AddMinutes(30))));
    }

    private static async Task<long> Count(NpgsqlDataSource db, string table)
    {
        await using var connection = await db.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<long>($"SELECT count(*) FROM {table}");
    }
}
