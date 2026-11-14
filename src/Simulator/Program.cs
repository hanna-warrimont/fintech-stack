using Confluent.Kafka;
using Portal.Simulator;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var kafka = builder.Configuration.GetConnectionString("kafka");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IProducer<string, string>>(_ => new ProducerBuilder<string, string>(new ProducerConfig
{
    BootstrapServers = kafka,
    Acks = Acks.All,
}).Build());
builder.Services.AddSingleton(new ChaosSettings
{
    TicksPerSecondPerInstrument = builder.Configuration.GetValue("Simulator:TicksPerSecondPerInstrument", 2.0),
});
builder.Services.AddSingleton<TickPublisher>();
builder.Services.AddSingleton<InstrumentCatalog>();
builder.Services.AddHostedService(sp => new InstrumentCatalogConsumer(
    sp.GetRequiredService<InstrumentCatalog>(),
    new ConsumerConfig
    {
        BootstrapServers = kafka,
        GroupId = $"simulator-{Guid.NewGuid():N}", // Broadcast: jede Instanz braucht alle Instrumente
        AutoOffsetReset = AutoOffsetReset.Earliest,
        EnableAutoCommit = false,
    },
    sp.GetRequiredService<ILogger<InstrumentCatalogConsumer>>()));
builder.Services.AddHostedService<SimulatorWorker>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapChaosEndpoints();
app.Run();
