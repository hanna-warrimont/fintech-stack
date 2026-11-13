using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Portal.Instruments.Application;
using Portal.Instruments.Application.Instruments;
using Portal.Instruments.Domain.Instruments;
using Portal.Instruments.Infrastructure;
using Portal.Instruments.Infrastructure.Outbox;
using Portal.Messaging;
using Portal.Messaging.Contracts;

namespace Portal.Tests.Instruments;

[Collection("infra")]
public sealed class OutboxDispatcherTests(KafkaFixture kafka, PostgresFixture postgres)
{
    [Fact]
    public async Task Dispatches_pending_messages_and_marks_them_processed()
    {
        // Wie das OTel-SDK im Betrieb: ohne Listener keine Activity, also kein traceparent.
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == KafkaTracing.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        var (db, topic) = await Setup();
        var keys = await InsertPending(db, topic, 3);
        using var producer = Producer(kafka.BootstrapServers);
        var dispatcher = Dispatcher(db, producer);

        Assert.Equal(3, await dispatcher.DispatchBatchAsync(CancellationToken.None));
        Assert.Equal(0, await dispatcher.DispatchBatchAsync(CancellationToken.None));

        var messages = Consume(topic, expected: 3);
        Assert.Equal(keys.Order(), messages.Select(m => m.Key).Order());
        Assert.All(messages, m => Assert.NotNull(JsonSerializer.Deserialize<Envelope<InstrumentSnapshot>>(m.Value, JsonSerializerOptions.Web)));
        Assert.All(messages, m => Assert.NotNull(m.Headers.FirstOrDefault(h => h.Key == "traceparent")));
        await using var connection = await db.OpenConnectionAsync();
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM outbox_messages WHERE processed_at IS NULL"));
        Assert.Empty(Consume(topic, expected: 0, fromOffset: 3));
    }

    [Fact]
    public async Task Two_dispatchers_never_send_a_message_twice()
    {
        var (db, topic) = await Setup();
        await InsertPending(db, topic, 200);
        using var producerA = Producer(kafka.BootstrapServers);
        using var producerB = Producer(kafka.BootstrapServers);

        await Task.WhenAll(DrainAsync(Dispatcher(db, producerA)), DrainAsync(Dispatcher(db, producerB)));

        var ids = Consume(topic, expected: 200)
            .Select(m => JsonSerializer.Deserialize<Envelope<InstrumentSnapshot>>(m.Value, JsonSerializerOptions.Web)!.MessageId)
            .ToList();
        Assert.Equal(200, ids.Count);
        Assert.Equal(200, ids.Distinct().Count());
    }

    [Fact]
    public async Task Unreachable_kafka_leaves_messages_pending()
    {
        var (db, topic) = await Setup();
        await InsertPending(db, topic, 2);
        var config = OutboxDispatcher.ProducerConfig("localhost:1");
        config.MessageTimeoutMs = 1000;
        using var producer = new ProducerBuilder<string, string>(config).Build();

        await Assert.ThrowsAnyAsync<KafkaException>(() => Dispatcher(db, producer).DispatchBatchAsync(CancellationToken.None));

        await using var connection = await db.OpenConnectionAsync();
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM outbox_messages WHERE processed_at IS NULL"));
    }

    [Fact]
    public async Task Publish_continues_trace_of_the_request()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var sp = new ServiceCollection().AddInstrumentApplication().AddInstrumentInfrastructure(connectionString).BuildServiceProvider();
        await using (var scope = sp.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<InstrumentDbContext>().Database.MigrateAsync();
        using var source = new ActivitySource("test-request");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == source.Name || s.Name == KafkaTracing.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        ActivityTraceId traceId;
        using (var request = source.StartActivity("POST /instruments")!)
        {
            traceId = request.TraceId;
            await using var scope = sp.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<CreateInstrumentHandler>().Handle(
                new CreateInstrumentCommand("DE0007164600", "716460", "SAP SE", InstrumentType.Share, "EUR"), CancellationToken.None);
        }

        var db = sp.GetRequiredService<NpgsqlDataSource>();
        var topic = $"instruments-test-{Guid.NewGuid():N}";
        await using (var connection = await db.OpenConnectionAsync())
            await connection.ExecuteAsync("UPDATE outbox_messages SET topic = @topic", new { topic });
        using var producer = Producer(kafka.BootstrapServers);
        await Dispatcher(db, producer).DispatchBatchAsync(CancellationToken.None);

        var traceparent = Encoding.UTF8.GetString(Consume(topic, expected: 1).Single().Headers.GetLastBytes("traceparent"));
        Assert.Contains(traceId.ToHexString(), traceparent);
    }

    private async Task<(NpgsqlDataSource Db, string Topic)> Setup()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var sp = new ServiceCollection().AddInstrumentInfrastructure(connectionString).BuildServiceProvider();
        await using (var scope = sp.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<InstrumentDbContext>().Database.MigrateAsync();
        return (sp.GetRequiredService<NpgsqlDataSource>(), $"instruments-test-{Guid.NewGuid():N}");
    }

    /// <summary>Outbox-Zeilen direkt per SQL – der Dispatcher kennt nur die Tabelle, nicht die Domäne.</summary>
    private static async Task<List<string>> InsertPending(NpgsqlDataSource db, string topic, int count)
    {
        await using var connection = await db.OpenConnectionAsync();
        var keys = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var id = Guid.CreateVersion7();
            var key = Guid.NewGuid().ToString("D");
            var envelope = new Envelope<InstrumentSnapshot>(id, InstrumentSnapshot.MessageType, 1, DateTimeOffset.UtcNow,
                new InstrumentSnapshot(Guid.Parse(key), "DE0007164600", null, $"I{i}", "Share", "EUR", "Active", 1));
            await connection.ExecuteAsync(
                "INSERT INTO outbox_messages (id, topic, key, type, payload, occurred_at) VALUES (@id, @topic, @key, @type, @payload::jsonb, now())",
                new { id, topic, key, type = InstrumentSnapshot.MessageType, payload = JsonSerializer.Serialize(envelope, JsonSerializerOptions.Web) });
            keys.Add(key);
        }

        return keys;
    }

    private static async Task DrainAsync(OutboxDispatcher dispatcher)
    {
        while (await dispatcher.DispatchBatchAsync(CancellationToken.None) > 0) { }
    }

    private static OutboxDispatcher Dispatcher(NpgsqlDataSource db, IProducer<string, string> producer) =>
        new(db, producer, TimeProvider.System, NullLogger<OutboxDispatcher>.Instance);

    private static IProducer<string, string> Producer(string bootstrap) =>
        new ProducerBuilder<string, string>(OutboxDispatcher.ProducerConfig(bootstrap)).Build();

    private List<Message<string, string>> Consume(string topic, int expected, int fromOffset = 0)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"test-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(topic);
        var messages = new List<Message<string, string>>();
        var deadline = DateTime.UtcNow.AddSeconds(expected == 0 ? 3 : 30);
        while ((expected == 0 || messages.Count < expected) && DateTime.UtcNow < deadline)
            if (consumer.Consume(TimeSpan.FromMilliseconds(500)) is { } r) messages.Add(r.Message);
        consumer.Close();
        if (expected > 0) Assert.Equal(expected, messages.Count);
        return messages.Skip(fromOffset).ToList();
    }
}
