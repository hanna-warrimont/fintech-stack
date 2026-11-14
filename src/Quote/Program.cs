using System.Net.ServerSentEvents;
using Confluent.Kafka;
using Portal.Messaging;
using Portal.Messaging.Contracts;
using Portal.Quote;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddSingleton<QuoteHub>();
builder.Services.AddHostedService(sp => new TickConsumer(
    sp.GetRequiredService<QuoteHub>(),
    // Eigene Group pro Instanz = Broadcast: jede Instanz sieht alle Ticks (siehe TickConsumer).
    new KafkaSettings(builder.Configuration).Consumer($"quote-{Guid.NewGuid():N}", AutoOffsetReset.Latest),
    sp.GetRequiredService<ILogger<TickConsumer>>()));

var app = builder.Build();

app.Lifetime.ApplicationStopping.Register(app.Services.GetRequiredService<QuoteHub>().CompleteAll);

app.MapGet("/quotes/{instrumentId:guid}", (Guid instrumentId, QuoteHub hub) =>
    hub.Latest(instrumentId) is { } tick ? Results.Ok(tick) : Results.NotFound());

app.MapGet("/quotes/{instrumentId:guid}/stream", (Guid instrumentId, QuoteHub hub, CancellationToken ct) =>
    TypedResults.ServerSentEvents(hub.Subscribe(instrumentId, ct).Select(t => new SseItem<Tick>(t, "tick"))));

app.MapDefaultEndpoints();

app.Run();
