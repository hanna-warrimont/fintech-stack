using System.Text.Json.Serialization;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Portal.Instruments.Api;
using Portal.Instruments.Application;
using Portal.Instruments.Infrastructure;
using Portal.Instruments.Infrastructure.Outbox;
using Portal.Messaging;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var kafka = builder.Configuration.GetConnectionString("kafka")!;
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services
    .AddInstrumentApplication()
    .AddInstrumentInfrastructure(builder.Configuration.GetConnectionString("instrumentdb")!);
builder.Services.AddSingleton<IProducer<string, string>>(_ =>
    new ProducerBuilder<string, string>(OutboxDispatcher.ProducerConfig(kafka)).Build());
builder.Services.AddHostedService<OutboxDispatcher>();

var app = builder.Build();

// Vor dem Start: Topic (kompaktiert) und Schema bereitstellen, dann Seed – alles bevor der Dispatcher läuft.
await Topics.EnsureTopicAsync(kafka, Topics.Instruments, config: Topics.Compacted);
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<InstrumentDbContext>().Database.MigrateAsync();
await SeedData.EnsureAsync(app.Services, CancellationToken.None);

app.MapInstrumentEndpoints();
app.MapDefaultEndpoints();

app.Run();
