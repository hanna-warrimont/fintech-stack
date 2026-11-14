using Confluent.Kafka;
using Portal.Messaging;
using Portal.Simulator;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var kafka = new KafkaSettings(builder.Configuration);
builder.Services.AddSingleton(kafka);
builder.Services.AddSingleton(TimeProvider.System);
// Idempotent: Ticks eines Takts gehen parallel raus – Reihenfolge pro Partition auch bei Retries
builder.Services.AddSingleton<IProducer<string, string>>(_ => new ProducerBuilder<string, string>(kafka.Producer()).Build());
builder.Services.AddSingleton(new ChaosSettings
{
    TicksPerSecondPerInstrument = builder.Configuration.GetValue("Simulator:TicksPerSecondPerInstrument", 2.0),
});
builder.Services.AddSingleton<TickPublisher>();
builder.Services.AddSingleton<InstrumentCatalog>();
builder.Services.AddHostedService(sp => new InstrumentCatalogConsumer(
    sp.GetRequiredService<InstrumentCatalog>(),
    kafka.Consumer($"simulator-{Guid.NewGuid():N}", AutoOffsetReset.Earliest), // Broadcast: jede Instanz braucht alle Instrumente
    sp.GetRequiredService<ILogger<InstrumentCatalogConsumer>>()));
builder.Services.AddSingleton<PriceBook>();
builder.Services.AddHostedService(sp => new CorporateActionConsumer(
    sp.GetRequiredService<PriceBook>(),
    kafka.Consumer($"simulator-ca-{Guid.NewGuid():N}", AutoOffsetReset.Latest), // Broadcast: jede Instanz führt ihre eigenen Kurse
    sp.GetRequiredService<ILogger<CorporateActionConsumer>>()));
builder.Services.AddHostedService<SimulatorWorker>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapChaosEndpoints();
app.Run();
