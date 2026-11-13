using System.Net.ServerSentEvents;
using Confluent.Kafka;
using Portal.Messaging.Contracts;
using Portal.Quote;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddSingleton<QuoteHub>();
builder.Services.AddHostedService(sp => new TickConsumer(
    sp.GetRequiredService<QuoteHub>(),
    new ConsumerConfig
    {
        BootstrapServers = builder.Configuration.GetConnectionString("kafka"),
        // Eigene Group pro Instanz = Broadcast: jede Instanz sieht alle Ticks (siehe TickConsumer).
        GroupId = $"quote-{Guid.NewGuid():N}",
        AutoOffsetReset = AutoOffsetReset.Latest,
        EnableAutoCommit = false,
    },
    sp.GetRequiredService<ILogger<TickConsumer>>()));

var app = builder.Build();

app.MapGet("/quotes/{instrumentId:guid}", (Guid instrumentId, QuoteHub hub) =>
    hub.Latest(instrumentId) is { } tick ? Results.Ok(tick) : Results.NotFound());

app.MapGet("/quotes/{instrumentId:guid}/stream", (Guid instrumentId, QuoteHub hub, CancellationToken ct) =>
    TypedResults.ServerSentEvents(hub.Subscribe(instrumentId, ct).Select(t => new SseItem<Tick>(t, "tick"))));

app.MapDefaultEndpoints();

app.Run();
