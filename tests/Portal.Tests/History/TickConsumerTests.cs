using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Portal.History;
using Portal.Messaging;
using Portal.Messaging.Contracts;
using HistoryTickConsumer = Portal.History.TickConsumer;

namespace Portal.Tests.History;

[Collection("infra")]
public sealed class TickConsumerTests(KafkaFixture kafka, PostgresFixture postgres) : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 14, 10, 0, 10, TimeSpan.Zero);
    private readonly IProducer<string, string> _producer =
        new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build();

    public void Dispose() => _producer.Dispose();

    [Fact]
    public async Task Ticks_become_candles_and_offsets_are_committed()
    {
        var (db, store, topic, group) = await Setup();
        var a = Guid.NewGuid();
        DeliveryResult<string, string>? last = null;
        for (var seq = 1; seq <= 3; seq++) last = await Publish(topic, new Tick(a, seq, 100m, seq, T0.AddSeconds(seq)));

        await Run(Consumer(store, topic, group), async () =>
        {
            await Eventually(async () => await Volume(db) == 6);
            await Eventually(async () => await Committed(group, last!.TopicPartition) == 3);
        });
    }

    [Fact]
    public async Task Poison_goes_to_dlt_and_next_message_is_processed()
    {
        var (db, store, topic, group) = await Setup();
        var a = Guid.NewGuid();
        await _producer.ProduceAsync(topic, new Message<string, string> { Key = a.ToString("D"), Value = "kein json" });
        await Publish(topic, new Tick(a, 1, 100m, 4, T0));

        var consumer = Consumer(store, topic, group);
        await Run(consumer, async () =>
        {
            await Eventually(async () => await Volume(db) == 4);
            var dlt = Assert.Single(ReadDlt(topic, expected: 1));
            Assert.Equal("kein json", dlt.Value);
            Assert.Equal("1", Header(dlt, "dlt-attempts"));
            Assert.False(consumer.ExecuteTask!.IsFaulted);
        });
    }

    /// <summary>Fehlende Tabelle = kein vorübergehender Fehler (42P01): Retry hilft nicht, also DLT.</summary>
    [Fact]
    public async Task Schema_error_goes_to_dlt_after_retries()
    {
        var (db, store, topic, group) = await Setup();
        var a = Guid.NewGuid();
        var consumer = Consumer(store, topic, group);
        await Run(consumer, async () =>
        {
            await Execute(db, "ALTER TABLE candles_1m RENAME TO candles_off");
            var failed = await Publish(topic, new Tick(a, 1, 100m, 1, T0));

            var dlt = Assert.Single(ReadDlt(topic, expected: 1));
            Assert.Equal("3", Header(dlt, "dlt-attempts"));
            await Eventually(async () => await Committed(group, failed.TopicPartition) == failed.Offset.Value + 1);

            await Execute(db, "ALTER TABLE candles_off RENAME TO candles_1m");
            await Publish(topic, new Tick(a, 2, 100m, 7, T0));
            await Eventually(async () => await Volume(db) == 7);
            Assert.False(consumer.ExecuteTask!.IsFaulted);
        });
    }

    /// <summary>
    /// Echter Ausfall: Die Datenbank nimmt keine Verbindungen mehr an. Das ist vorübergehend – der Tick darf nicht ins DLT
    /// (dort wäre er verloren, weil neuere Ticks die seq-Grenze anheben), sondern wird wiederholt, bis die DB zurück ist.
    /// </summary>
    [Fact]
    public async Task Database_outage_is_retried_not_dead_lettered()
    {
        var (db, store, topic, group) = await Setup();
        var a = Guid.NewGuid();
        var consumer = Consumer(store, topic, group);
        await Run(consumer, async () =>
        {
            await Publish(topic, new Tick(a, 1, 100m, 1, T0));
            await Eventually(async () => await Volume(db) == 1);

            var database = new NpgsqlConnectionStringBuilder(db.ConnectionString).Database!;
            await Admin($"ALTER DATABASE {database} ALLOW_CONNECTIONS false",
                $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{database}'");
            await Publish(topic, new Tick(a, 2, 100m, 7, T0.AddSeconds(1)));
            await Task.Delay(3000); // länger als alle Versuche des DeadLetter (200 ms + 1 s)
            await Admin($"ALTER DATABASE {database} ALLOW_CONNECTIONS true");
            db.Clear(); // abgeschossene Verbindungen im Pool nicht für die Prüfung unten verwenden

            await Eventually(async () => await Volume(db) == 8); // Tick 2 kam an – im DLT wäre er übersprungen
            Assert.False(consumer.ExecuteTask!.IsFaulted);
        });
        Assert.Empty(ReadDlt(topic, expected: 1, wait: TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Two_consumers_in_one_group_share_partitions()
    {
        var (_, store, topic, group) = await Setup();
        var consumers = new[] { Consumer(store, topic, group), Consumer(store, topic, group) };
        foreach (var c in consumers) await c.StartAsync(CancellationToken.None);
        try
        {
            using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafka.BootstrapServers }).Build();
            var instruments = Enumerable.Range(0, 12).Select(_ => Guid.NewGuid()).ToList();
            var seq = 0L;
            List<MemberDescription> members = [];
            await Eventually(async () =>
            {
                foreach (var id in instruments) await Publish(topic, new Tick(id, ++seq, 100m, 1, T0));
                var description = await admin.DescribeConsumerGroupsAsync([group]);
                members = description.ConsumerGroupDescriptions.Single().Members;
                return members.Count == 2 && members.All(m => m.Assignment.TopicPartitions.Count > 0);
            });

            var partitions = members.SelectMany(m => m.Assignment.TopicPartitions.Select(tp => tp.Partition.Value)).ToList();
            Assert.Equal(Enumerable.Range(0, 6), partitions.Order());
        }
        finally
        {
            foreach (var c in consumers) await c.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Empty_store_rebuilds_from_beginning()
    {
        var (db, store, topic, group) = await Setup();
        var a = Guid.NewGuid();
        await Publish(topic, new Tick(a, 1, 100m, 2, T0));
        var last = await Publish(topic, new Tick(a, 2, 101m, 3, T0.AddSeconds(5)));

        await Run(Consumer(store, topic, group), () =>
            Eventually(async () => await Volume(db) == 5 && await Committed(group, last.TopicPartition) > last.Offset.Value));

        await store.ClearAsync(CancellationToken.None);
        await Run(Consumer(store, topic, group), () => Eventually(async () => await Volume(db) == 5));
    }

    private async Task<(NpgsqlDataSource Db, CandleStore Store, string Topic, string Group)> Setup()
    {
        var db = NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync());
        await HistorySchema.EnsureAsync(db, default);
        var topic = $"ticks-test-{Guid.NewGuid():N}";
        await Topics.EnsureTopicAsync(kafka.BootstrapServers, topic);
        await Topics.EnsureTopicAsync(kafka.BootstrapServers, DeadLetter.TopicFor(topic, "history"));
        return (db, new CandleStore(db), topic, $"history-test-{Guid.NewGuid():N}");
    }

    private HistoryTickConsumer Consumer(CandleStore store, string topic, string group) =>
        new(store, new DeadLetter(_producer, "history", NullLogger.Instance), new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = group,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }, NullLogger<HistoryTickConsumer>.Instance, topic);

    private static async Task Run(HistoryTickConsumer consumer, Func<Task> body)
    {
        await consumer.StartAsync(CancellationToken.None);
        try
        {
            await body();
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    private Task<DeliveryResult<string, string>> Publish(string topic, Tick tick) =>
        _producer.ProduceAsync(topic, new Message<string, string>
        {
            Key = tick.InstrumentId.ToString("D"),
            Value = JsonSerializer.Serialize(tick, JsonSerializerOptions.Web),
        });

    private async Task Admin(params string[] statements)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString("postgres", "postgres", "postgres"));
        await connection.OpenAsync();
        foreach (var sql in statements) await connection.ExecuteAsync(sql);
    }

    private List<Message<string, string>> ReadDlt(string topic, int expected, TimeSpan? wait = null)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"dlt-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(DeadLetter.TopicFor(topic, "history"));
        var messages = new List<Message<string, string>>();
        var deadline = DateTime.UtcNow + (wait ?? TimeSpan.FromSeconds(30));
        while (messages.Count < expected && DateTime.UtcNow < deadline)
            if (consumer.Consume(TimeSpan.FromMilliseconds(200)) is { } r) messages.Add(r.Message);
        consumer.Close();
        return messages;
    }

    private static string? Header(Message<string, string> m, string key) =>
        m.Headers.TryGetLastBytes(key, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;

    private static async Task<long> Volume(NpgsqlDataSource db)
    {
        await using var connection = await db.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<long?>("SELECT sum(volume)::bigint FROM candles_1m") ?? 0;
    }

    private static async Task Execute(NpgsqlDataSource db, string sql)
    {
        await using var connection = await db.OpenConnectionAsync();
        await connection.ExecuteAsync(sql);
    }

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
