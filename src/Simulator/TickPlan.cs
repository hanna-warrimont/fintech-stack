using Portal.Messaging.Contracts;

namespace Portal.Simulator;

/// <summary>Welche Ticks ein Instrument in einem Takt erzeugt – normal, doppelt oder mit einem verspäteten Extra-Tick.</summary>
public static class TickPlan
{
    private static readonly TimeSpan Delay = TimeSpan.FromMinutes(2);

    public static IReadOnlyList<Tick> For(Guid instrumentId, decimal price, long volume, DateTimeOffset now, ref long seq, ChaosSettings chaos)
    {
        var tick = new Tick(instrumentId, ++seq, price, volume, now);
        // Duplikat: derselbe Tick (gleiche seq) zweimal – wie ein Producer-Retry ohne Idempotenz. History muss ihn verwerfen.
        var ticks = chaos.Duplicates ? new List<Tick> { tick, tick } : [tick];
        // Verspätet: jeder 10. Takt bringt zusätzlich einen Tick mit Event-Time vor 2 Minuten – wie ein Feed,
        // der nach einer Störung Nachzügler liefert. Neue seq, sonst wäre es ein Duplikat.
        if (chaos.Late && seq % 10 == 0)
            ticks.Add(new Tick(instrumentId, ++seq, price, volume, now - Delay));
        return ticks;
    }
}
