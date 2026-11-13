using Confluent.Kafka;
using Npgsql;
using Portal.Search;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var db = NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("searchdb")!);
builder.Services.AddSingleton(db);
builder.Services.AddSingleton<InstrumentProjection>();
builder.Services.AddSingleton<SearchQuery>();
builder.Services.AddHostedService(sp => new SnapshotConsumer(
    sp.GetRequiredService<InstrumentProjection>(),
    new ConsumerConfig
    {
        BootstrapServers = builder.Configuration.GetConnectionString("kafka"),
        // Gemeinsame Group = Arbeitsteilung: mehrere Search-Instanzen teilen sich die Partitionen.
        GroupId = "search",
        AutoOffsetReset = AutoOffsetReset.Earliest,
        EnableAutoCommit = false,
    },
    sp.GetRequiredService<ILogger<SnapshotConsumer>>()));

var app = builder.Build();

await SearchSchema.EnsureAsync(db, CancellationToken.None);

app.MapGet("/search", (string? q, SearchQuery query, CancellationToken ct) => query.Handle(q, ct));
app.MapDefaultEndpoints();

app.Run();
