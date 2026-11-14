using Portal.Messaging;

namespace Portal.Simulator;

public sealed class SimulatorWorker(
    TickPublisher publisher, InstrumentCatalog catalog, ChaosSettings chaos, IConfiguration config, TimeProvider time,
    ILogger<SimulatorWorker> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Topics.EnsureTopicAsync(config.GetConnectionString("kafka")!, Topics.Ticks);

        var rate = chaos.TicksPerSecondPerInstrument;
        var rng = new Random();
        var prices = new Dictionary<Guid, decimal>();
        // seq pro Instrument: Die Dedupe in History gilt pro Instrument, und verspätete Ticks verbrauchen Extra-Nummern.
        // ponytail: Start bei Unix-ms → monoton über Neustarts, solange < 1000 Ticks/s pro Instrument; sonst persistente seq
        var seqs = new Dictionary<Guid, long>();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1 / rate), time);
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (chaos.TicksPerSecondPerInstrument != rate)
            {
                rate = chaos.TicksPerSecondPerInstrument;
                timer.Period = TimeSpan.FromSeconds(1 / rate);
            }
            if (chaos.Paused) continue;

            foreach (var instrument in catalog.Active)
            {
                var price = PriceWalk.Next(prices.GetValueOrDefault(instrument.Id, StartPrices.For(instrument.Isin)), rng);
                prices[instrument.Id] = price;
                var seq = seqs.GetValueOrDefault(instrument.Id, time.GetUtcNow().ToUnixTimeMilliseconds());
                var ticks = TickPlan.For(instrument.Id, price, rng.Next(1, 501), time.GetUtcNow(), ref seq, chaos);
                seqs[instrument.Id] = seq;
                foreach (var tick in ticks)
                {
                    try
                    {
                        await publisher.PublishAsync(tick, ct);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        log.LogWarning(e, "Tick für {InstrumentId} nicht gesendet", instrument.Id);
                    }
                }
            }
        }
    }
}
