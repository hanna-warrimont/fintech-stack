using System.Data.Common;
using Confluent.Kafka;
using Npgsql;
using Portal.Messaging;
using Portal.Search;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var db = NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("searchdb")!);
builder.Services.AddSingleton(db);
builder.Services.AddSingleton<DbDataSource>(db);
builder.AddDatabaseHealthCheck();
builder.Services.AddSingleton<InstrumentProjection>();
builder.Services.AddSingleton<SearchQuery>();
builder.Services.AddHostedService(sp => new SnapshotConsumer(
    sp.GetRequiredService<InstrumentProjection>(),
    // Gemeinsame Group = Arbeitsteilung: mehrere Search-Instanzen teilen sich die Partitionen.
    new KafkaSettings(builder.Configuration).Consumer("search", AutoOffsetReset.Earliest),
    sp.GetRequiredService<ILogger<SnapshotConsumer>>()));

var app = builder.Build();

await SearchSchema.EnsureAsync(db, CancellationToken.None);

app.MapGet("/search", (string? q, SearchQuery query, CancellationToken ct) => query.Handle(q, ct));
app.MapDefaultEndpoints();

app.Run();
