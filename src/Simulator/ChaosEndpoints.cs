using Confluent.Kafka;
using Portal.Messaging;

namespace Portal.Simulator;

// Nur lokal: Störungen gezielt auslösen. Nicht über das BFF erreichbar.
public static class ChaosEndpoints
{
    public static void MapChaosEndpoints(this WebApplication app)
    {
        var chaos = app.MapGroup("/chaos");
        chaos.MapGet("/", (ChaosSettings s) => State(s));
        chaos.MapPost("/duplicates", (bool on, ChaosSettings s) => { s.Duplicates = on; return State(s); });
        chaos.MapPost("/late", (bool on, ChaosSettings s) => { s.Late = on; return State(s); });
        chaos.MapPost("/pause", (bool on, ChaosSettings s) => { s.Paused = on; return State(s); });
        chaos.MapPost("/rate", (double ticksPerSecond, ChaosSettings s) =>
        {
            if (ticksPerSecond is < 0.1 or > 1000)
                return Results.Problem("ticksPerSecond muss zwischen 0.1 und 1000 liegen.", statusCode: 400);
            s.TicksPerSecondPerInstrument = ticksPerSecond;
            return Results.Ok(State(s));
        });
        // Poison: eine Nachricht, die kein Consumer lesen kann. Landet bei History sofort im DLT.
        chaos.MapPost("/poison", async (InstrumentCatalog catalog, IProducer<string, string> producer, CancellationToken ct) =>
        {
            if (catalog.Active.FirstOrDefault() is not { } instrument)
                return Results.Problem("Kein aktives Instrument bekannt.", statusCode: 409);
            var delivery = await producer.ProduceAsync(Topics.Ticks,
                new Message<string, string> { Key = instrument.Id.ToString("D"), Value = "kein json" }, ct);
            return Results.Ok(new { delivery.Partition.Value, Offset = delivery.Offset.Value });
        });
    }

    private static object State(ChaosSettings s) =>
        new { s.Duplicates, s.Late, s.Paused, s.TicksPerSecondPerInstrument };
}
