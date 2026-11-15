using Confluent.Kafka;
using Npgsql;
using Portal.Messaging;
using Portal.Search;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Aspire-Integration: Datenquelle aus ConnectionStrings:searchdb (fehlt sie, scheitert der Start sofort), dazu Health Check
// für Readiness sowie Traces und Metriken von Npgsql. Registriert NpgsqlDataSource und DbDataSource.
builder.AddNpgsqlDataSource("searchdb");
builder.Services.AddSingleton<InstrumentProjection>();
builder.Services.AddSingleton<SearchQuery>();
builder.Services.AddSingleton(TimeProvider.System);
var kafka = new KafkaSettings(builder.Configuration);
builder.Services.AddSingleton<IProducer<string, string>>(_ => new ProducerBuilder<string, string>(kafka.Producer()).Build());
builder.Services.AddHostedService(sp => new SnapshotConsumer(
    sp.GetRequiredService<InstrumentProjection>(),
    new DeadLetter(sp.GetRequiredService<IProducer<string, string>>(), SnapshotConsumer.Group, sp.GetRequiredService<ILogger<DeadLetter>>()),
    // Gemeinsame Group = Arbeitsteilung: mehrere Search-Instanzen teilen sich die Partitionen.
    kafka.Consumer(SnapshotConsumer.Group, AutoOffsetReset.Earliest),
    sp.GetRequiredService<ILogger<SnapshotConsumer>>()));
// Inbox 30 Tage: Wiederholte Nachrichten danach fängt die revision ab.
builder.Services.AddHostedService(sp => ActivatorUtilities.CreateInstance<RetentionJob>(sp, RetentionJob.Inbox, TimeSpan.FromDays(30)));

var app = builder.Build();

await SearchSchema.EnsureAsync(app.Services.GetRequiredService<NpgsqlDataSource>(), CancellationToken.None);
await kafka.EnsureTopicAsync(DeadLetter.TopicFor(Topics.Instruments, SnapshotConsumer.Group));

app.MapGet("/search", (string? q, SearchQuery query, CancellationToken ct) => query.Handle(q, ct));
app.MapDefaultEndpoints();

app.Run();
