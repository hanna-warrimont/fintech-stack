using Confluent.Kafka;
using Portal.Simulator;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IProducer<string, string>>(_ => new ProducerBuilder<string, string>(new ProducerConfig
{
    BootstrapServers = builder.Configuration.GetConnectionString("kafka"),
    Acks = Acks.All,
}).Build());
builder.Services.AddSingleton<TickPublisher>();
builder.Services.AddHostedService<SimulatorWorker>();

var app = builder.Build();

app.MapGet("/instruments", () => SeedInstruments.All.Select(ToDto));
app.MapGet("/instruments/{id:guid}", (Guid id) =>
    SeedInstruments.All.FirstOrDefault(i => i.Id == id) is { } i ? Results.Ok(ToDto(i)) : Results.NotFound());
app.MapDefaultEndpoints();

app.Run();

static object ToDto(SeedInstrument i) => new { i.Id, i.Isin, i.Name, i.Currency };
