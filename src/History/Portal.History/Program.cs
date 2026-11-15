using System.Data.Common;
using Confluent.Kafka;
using Npgsql;
using Portal.History;
using Portal.Messaging;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

const string group = "history";
var kafka = new KafkaSettings(builder.Configuration);
var db = NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("historydb")!);
builder.Services.AddSingleton(db);
builder.Services.AddSingleton<DbDataSource>(db);
builder.AddDatabaseHealthCheck();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CandleStore>();
builder.Services.AddSingleton<CandleQuery>();
builder.Services.AddSingleton<IProducer<string, string>>(_ =>
    new ProducerBuilder<string, string>(kafka.Producer()).Build());
builder.Services.AddHostedService(sp => new TickConsumer(
    sp.GetRequiredService<CandleStore>(),
    new DeadLetter(sp.GetRequiredService<IProducer<string, string>>(), group, sp.GetRequiredService<ILogger<DeadLetter>>()),
    // Gemeinsame Group = Arbeitsteilung: mehrere History-Instanzen teilen sich die Partitionen.
    kafka.Consumer(group, AutoOffsetReset.Earliest),
    sp.GetRequiredService<ILogger<TickConsumer>>()));

builder.Services.AddSingleton<AdjustmentStore>();
builder.Services.AddHostedService(sp => new CorporateActionConsumer(
    sp.GetRequiredService<AdjustmentStore>(),
    new DeadLetter(sp.GetRequiredService<IProducer<string, string>>(), CorporateActionConsumer.Group, sp.GetRequiredService<ILogger<DeadLetter>>()),
    kafka.Consumer(CorporateActionConsumer.Group, AutoOffsetReset.Earliest), // neue Instanz/DB: alle Splits von Anfang an
    sp.GetRequiredService<ILogger<CorporateActionConsumer>>()));

var app = builder.Build();

await HistorySchema.EnsureAsync(db, CancellationToken.None);
await kafka.EnsureTopicAsync(DeadLetter.TopicFor(Topics.Ticks, group));
await kafka.EnsureTopicAsync(DeadLetter.TopicFor(Topics.CorporateActions, CorporateActionConsumer.Group));

app.MapGet("/history/{instrumentId:guid}", async (Guid instrumentId, string? interval, CandleQuery query, CancellationToken ct) =>
{
    interval ??= "1m";
    return CandleQuery.IsValidInterval(interval)
        ? Results.Ok(await query.Handle(instrumentId, interval, ct))
        : Results.Problem("interval muss 1m, 1h oder 1d sein.", statusCode: 400);
});
app.MapDefaultEndpoints();

app.Run();
