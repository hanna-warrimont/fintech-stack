using Portal.Messaging;
using Portal.Messaging.Contracts;

namespace Portal.Simulator;

public sealed class SimulatorWorker(TickPublisher publisher, IConfiguration config, TimeProvider time, ILogger<SimulatorWorker> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Topics.EnsureTopicAsync(config.GetConnectionString("kafka")!, Topics.Ticks);

        var rate = config.GetValue("Simulator:TicksPerSecondPerInstrument", 2.0);
        var rng = new Random();
        var prices = SeedInstruments.All.ToDictionary(i => i.Id, i => i.StartPrice);
        // ponytail: monoton über Neustarts, solange < 1000 Ticks/s pro Instrument; sonst persistente seq
        var seq = time.GetUtcNow().ToUnixTimeMilliseconds();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1 / rate), time);
        while (await timer.WaitForNextTickAsync(ct))
        {
            seq++;
            foreach (var instrument in SeedInstruments.All)
            {
                prices[instrument.Id] = PriceWalk.Next(prices[instrument.Id], rng);
                try
                {
                    await publisher.PublishAsync(
                        new Tick(instrument.Id, seq, prices[instrument.Id], rng.Next(1, 501), time.GetUtcNow()), ct);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.LogWarning(e, "Tick für {InstrumentId} nicht gesendet", instrument.Id);
                }
            }
        }
    }
}
