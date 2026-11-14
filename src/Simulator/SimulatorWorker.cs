using Portal.Messaging;
using Portal.Messaging.Contracts;

namespace Portal.Simulator;

public sealed class SimulatorWorker(
    TickPublisher publisher, InstrumentCatalog catalog, PriceBook prices, ChaosSettings chaos, IConfiguration config, TimeProvider time,
    ILogger<SimulatorWorker> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        async Task Send(Tick tick)
        {
            try
            {
                await publisher.PublishAsync(tick, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "Tick für {InstrumentId} nicht gesendet", tick.InstrumentId);
            }
        }

        await Topics.EnsureTopicAsync(config.GetConnectionString("kafka")!, Topics.Ticks);

        var rate = chaos.TicksPerSecondPerInstrument;
        var rng = new Random();
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

            // Alle Ticks eines Takts gemeinsam senden und gemeinsam auf die Bestätigung warten: Einzeln gewartet
            // schafft der Simulator nur ~200 Ticks/s (eine Broker-Runde pro Tick) – zu wenig für das Lag-Experiment.
            var sends = new List<Task>();
            foreach (var instrument in catalog.Active)
            {
                var price = prices.Next(instrument, rng);
                var seq = seqs.GetValueOrDefault(instrument.Id, time.GetUtcNow().ToUnixTimeMilliseconds());
                var ticks = TickPlan.For(instrument.Id, price, rng.Next(1, 501), time.GetUtcNow(), ref seq, chaos);
                seqs[instrument.Id] = seq;
                sends.AddRange(ticks.Select(Send));
            }
            await Task.WhenAll(sends);
        }
    }
}
