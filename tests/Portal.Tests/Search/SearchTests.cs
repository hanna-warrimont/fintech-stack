using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Portal.Messaging;
using Portal.Messaging.Contracts;
using Portal.Search;

namespace Portal.Tests.Search;

[Collection("infra")]
public sealed class SearchTests(KafkaFixture kafka, PostgresFixture postgres)
{
    private static readonly Guid Sap = Guid.NewGuid(), Siemens = Guid.NewGuid(), Allianz = Guid.NewGuid(), Msci = Guid.NewGuid();

    [Fact]
    public async Task Apply_is_idempotent_per_message()
    {
        var (db, projection) = await Setup();
        var snapshot = Snap(Sap, "SAP SE", 1);

        Assert.True(await projection.ApplyAsync(snapshot, CancellationToken.None));
        Assert.False(await projection.ApplyAsync(snapshot, CancellationToken.None));

        Assert.Equal(1, await Count(db, "instruments"));
        Assert.Equal(1, await Count(db, "processed_messages"));
    }

    [Fact]
    public async Task Older_revision_does_not_overwrite_newer()
    {
        var (db, projection) = await Setup();

        await projection.ApplyAsync(Snap(Sap, "Neu", 2), CancellationToken.None);
        await projection.ApplyAsync(Snap(Sap, "Alt", 1), CancellationToken.None);

        await using var connection = await db.OpenConnectionAsync();
        Assert.Equal("Neu", await connection.ExecuteScalarAsync<string>("SELECT name FROM instruments"));
    }

    [Fact]
    public async Task Search_finds_by_name_prefix_isin_and_wkn()
    {
        var (db, query) = await SeededQuery();

        Assert.Equal([Siemens], await Ids(query, "sie"));
        Assert.Equal([Sap], await Ids(query, "DE0007164600"));
        Assert.Equal([Sap], await Ids(query, "716460"));
        Assert.Equal([Msci], await Ids(query, "msci world"));
        Assert.Equal([Sap], await Ids(query, "SAP SE"));
        Assert.Equal([Allianz, Msci, Sap, Siemens], await Ids(query, ""));
        Assert.Equal([Allianz, Msci, Sap, Siemens], await Ids(query, null));
    }

    [Fact]
    public async Task Search_handles_special_characters()
    {
        var (db, query) = await SeededQuery();

        foreach (var noWords in new[] { "'", "&", "%", "   " })
            Assert.Equal(4, (await query.Handle(noWords, CancellationToken.None)).Count); // keine Wörter = keine Einschränkung
        Assert.Equal([Sap], await Ids(query, "sap:*"));
        Assert.Empty(await query.Handle("a'); DROP TABLE instruments; --", CancellationToken.None));
        Assert.Equal(4, await Count(db, "instruments"));
    }

    [Fact]
    public async Task Consumer_redelivers_after_crash_before_commit_and_inbox_dedupes()
    {
        var (db, projection) = await Setup();
        var topic = await Topic();
        var group = $"search-test-{Guid.NewGuid():N}";
        var delivered = await Produce(topic, Snap(Sap, "SAP SE", 1));

        // „Absturz“: verarbeiten, aber nicht committen
        using (var crashed = new ConsumerBuilder<string, string>(Config(group)).Build())
        {
            crashed.Subscribe(topic);
            var result = crashed.Consume(TimeSpan.FromSeconds(30))!;
            Assert.True(await projection.ApplyAsync(JsonSerializer.Deserialize<Envelope<InstrumentSnapshot>>(result.Message.Value, JsonSerializerOptions.Web)!, CancellationToken.None));
            crashed.Close();
        }

        var consumer = Consumer(projection, topic, group);
        await consumer.StartAsync(CancellationToken.None);
        try
        {
            await Eventually(async () => await Committed(group, delivered.TopicPartition) > delivered.Offset.Value);
            Assert.Equal(1, await Count(db, "instruments"));
            Assert.Equal(1, await Count(db, "processed_messages"));
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Empty_projection_rebuilds_from_beginning()
    {
        var (db, projection) = await Setup();
        var topic = await Topic();
        var group = $"search-test-{Guid.NewGuid():N}";
        await Produce(topic, Snap(Sap, "SAP SE", 1));
        var last = await Produce(topic, Snap(Siemens, "Siemens AG", 1, "DE0007236101"));

        var first = Consumer(projection, topic, group);
        await first.StartAsync(CancellationToken.None);
        await Eventually(async () => await Count(db, "instruments") == 2 && await Committed(group, last.TopicPartition) > last.Offset.Value);
        await first.StopAsync(CancellationToken.None);

        await projection.ClearAsync(CancellationToken.None);
        var second = Consumer(projection, topic, group);
        await second.StartAsync(CancellationToken.None);
        try
        {
            await Eventually(async () => await Count(db, "instruments") == 2);
        }
        finally
        {
            await second.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Consumer_skips_invalid_messages()
    {
        var (db, projection) = await Setup();
        var topic = await Topic();
        using (var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build())
        {
            await producer.ProduceAsync(topic, new Message<string, string> { Key = "x", Value = "kein json" });
            await producer.ProduceAsync(topic, new Message<string, string> { Key = "x", Value = null! });
            await producer.ProduceAsync(topic, new Message<string, string>
            {
                Key = "x",
                Value = JsonSerializer.Serialize(Snap(Sap, "fremd", 1) with { Type = "Other" }, JsonSerializerOptions.Web),
            });
        }
        await Produce(topic, Snap(Siemens, "Siemens AG", 1, "DE0007236101"));

        var consumer = Consumer(projection, topic, $"search-test-{Guid.NewGuid():N}");
        await consumer.StartAsync(CancellationToken.None);
        try
        {
            await Eventually(async () => await Count(db, "instruments") == 1);
            await using var connection = await db.OpenConnectionAsync();
            Assert.Equal(Siemens, await connection.ExecuteScalarAsync<Guid>("SELECT id FROM instruments"));
            Assert.False(consumer.ExecuteTask!.IsFaulted);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Failed_apply_is_retried_not_skipped()
    {
        var (db, projection) = await Setup();
        var topic = await Topic();
        var group = $"search-test-{Guid.NewGuid():N}";
        await Produce(topic, Snap(Sap, "SAP SE", 1));
        var consumer = Consumer(projection, topic, group);
        await consumer.StartAsync(CancellationToken.None);
        try
        {
            await Eventually(async () => await Count(db, "instruments") == 1);
            await Execute(db, "ALTER TABLE instruments RENAME TO instruments_off");

            var second = await Produce(topic, Snap(Siemens, "Siemens AG", 1, "DE0007236101"));
            await Task.Delay(3000);

            Assert.Equal(1, await Count(db, "instruments_off"));
            var committed = await Committed(group, second.TopicPartition);
            Assert.True(committed == Offset.Unset.Value || committed <= second.Offset.Value, $"committed {committed}");
            Assert.False(consumer.ExecuteTask!.IsFaulted);

            await Execute(db, "ALTER TABLE instruments_off RENAME TO instruments");
            await Eventually(async () => await Count(db, "instruments") == 2);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    private async Task<(NpgsqlDataSource Db, InstrumentProjection Projection)> Setup()
    {
        var db = NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync());
        await SearchSchema.EnsureAsync(db, CancellationToken.None);
        return (db, new InstrumentProjection(db));
    }

    private async Task<(NpgsqlDataSource Db, SearchQuery Query)> SeededQuery()
    {
        var (db, projection) = await Setup();
        foreach (var snapshot in new[]
                 {
                     Snap(Sap, "SAP SE", 1, "DE0007164600", "716460"),
                     Snap(Siemens, "Siemens AG", 1, "DE0007236101", "723610"),
                     Snap(Allianz, "Allianz SE", 1, "DE0008404005", "840400"),
                     Snap(Msci, "iShares Core MSCI World UCITS ETF", 1, "IE00B4L5Y983", "A0RPWH", "Etf"),
                 })
            await projection.ApplyAsync(snapshot, CancellationToken.None);
        return (db, new SearchQuery(db));
    }

    private static Envelope<InstrumentSnapshot> Snap(Guid id, string name, int revision, string isin = "DE0007164600", string? wkn = null, string type = "Share") =>
        new(Guid.CreateVersion7(), InstrumentSnapshot.MessageType, InstrumentSnapshot.MessageVersion, DateTimeOffset.UtcNow,
            new InstrumentSnapshot(id, isin, wkn, name, type, "EUR", "Active", revision));

    private static async Task<List<Guid>> Ids(SearchQuery query, string? q) =>
        (await query.Handle(q, CancellationToken.None)).Select(h => h.Id).ToList();

    private static async Task<int> Count(NpgsqlDataSource db, string table)
    {
        await using var connection = await db.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>($"SELECT count(*) FROM {table}");
    }

    private static async Task Execute(NpgsqlDataSource db, string sql)
    {
        await using var connection = await db.OpenConnectionAsync();
        await connection.ExecuteAsync(sql);
    }

    private async Task<string> Topic()
    {
        var topic = $"instruments-test-{Guid.NewGuid():N}";
        await Topics.EnsureTopicAsync(kafka.BootstrapServers, topic, config: Topics.Compacted);
        return topic;
    }

    private async Task<DeliveryResult<string, string>> Produce(string topic, Envelope<InstrumentSnapshot> envelope)
    {
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        return await producer.ProduceAsync(topic, new Message<string, string>
        {
            Key = envelope.Payload.Id.ToString("D"),
            Value = JsonSerializer.Serialize(envelope, JsonSerializerOptions.Web),
        });
    }

    private ConsumerConfig Config(string group) => new()
    {
        BootstrapServers = kafka.BootstrapServers,
        GroupId = group,
        AutoOffsetReset = AutoOffsetReset.Earliest,
        EnableAutoCommit = false,
    };

    private SnapshotConsumer Consumer(InstrumentProjection projection, string topic, string group) =>
        new(projection, Config(group), NullLogger<SnapshotConsumer>.Instance, topic);

    private async Task<long> Committed(string group, TopicPartition partition)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        var result = await admin.ListConsumerGroupOffsetsAsync([new ConsumerGroupTopicPartitions(group, [partition])]);
        return result.Single().Partitions.Single().Offset.Value;
    }

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Bedingung nicht innerhalb von 30 s erfüllt");
            await Task.Delay(250);
        }
    }
}
