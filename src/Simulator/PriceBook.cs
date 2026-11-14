using System.Collections.Concurrent;
using Portal.Messaging.Contracts;

namespace Portal.Simulator;

/// <summary>Laufender Kurs pro Instrument. Geteilt zwischen Worker (Zufallspfad) und Split-Consumer (neues Kursniveau).</summary>
public sealed class PriceBook
{
    private readonly ConcurrentDictionary<Guid, decimal> _prices = new();

    public decimal Next(InstrumentSnapshot instrument, Random rng) =>
        _prices.AddOrUpdate(instrument.Id,
            _ => PriceWalk.Next(StartPrices.For(instrument.Isin), rng),
            (_, current) => PriceWalk.Next(current, rng));

    /// <summary>Split wirksam: 1:4 → Kurs × 0,25. Noch kein Kurs = nichts zu tun (der Startkurs kommt dann ohnehin neu).</summary>
    public void ApplySplit(Guid instrumentId, decimal factor)
    {
        // ponytail: nach Simulator-Neustart beginnt der Kurs wieder beim Startkurs (vor dem Split); Ausbau: letzten Kurs aus History holen
        if (_prices.TryGetValue(instrumentId, out var current))
            _prices.TryUpdate(instrumentId, Math.Max(0.01m, Math.Round(current * factor, 2)), current);
    }
}
