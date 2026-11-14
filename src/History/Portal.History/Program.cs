using Confluent.Kafka;
using Npgsql;
using Portal.History;
using Portal.Messaging;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

const string group = "history";
var kafka = builder.Configuration.GetConnectionString("kafka")!;
var db = NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("historydb")!);
builder.Services.AddSingleton(db);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CandleStore>();
builder.Services.AddSingleton<CandleQuery>();
builder.Services.AddSingleton<IProducer<string, string>>(_ =>
    new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafka, Acks = Acks.All }).Build());
builder.Services.AddHostedService(sp => new TickConsumer(
    sp.GetRequiredService<CandleStore>(),
    new DeadLetter(sp.GetRequiredService<IProducer<string, string>>(), group, sp.GetRequiredService<ILogger<DeadLetter>>()),
    new ConsumerConfig
    {
        BootstrapServers = kafka,
        // Gemeinsame Group = Arbeitsteilung: mehrere History-Instanzen teilen sich die Partitionen.
        GroupId = group,
        AutoOffsetReset = AutoOffsetReset.Earliest,
        EnableAutoCommit = false,
    },
    sp.GetRequiredService<ILogger<TickConsumer>>()));

var app = builder.Build();

await HistorySchema.EnsureAsync(db, CancellationToken.None);
await Topics.EnsureTopicAsync(kafka, DeadLetter.TopicFor(Topics.Ticks, group));

app.MapGet("/history/{instrumentId:guid}", async (Guid instrumentId, string? interval, CandleQuery query, CancellationToken ct) =>
{
    interval ??= "1m";
    return CandleQuery.IsValidInterval(interval)
        ? Results.Ok(await query.Handle(instrumentId, interval, ct))
        : Results.Problem("interval muss 1m, 1h oder 1d sein.", statusCode: 400);
});
app.MapDefaultEndpoints();

app.Run();
