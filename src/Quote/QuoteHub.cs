using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Portal.Messaging.Contracts;

namespace Portal.Quote;

/// <summary>
/// Hält den letzten Kurs pro Instrument und verteilt neue Ticks an alle offenen SSE-Verbindungen (Fan-out).
/// Jeder Abonnent hat einen eigenen kleinen Puffer: Ein langsamer Client verliert alte Ticks, bremst aber niemanden.
/// </summary>
// ponytail: nur im Speicher, nach Neustart leer bis zum nächsten Tick; Ausbau: kompaktiertes quotes.latest
public sealed class QuoteHub
{
    private const int BufferPerSubscriber = 16;

    private readonly ConcurrentDictionary<Guid, Tick> _latest = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Channel<Tick>, byte>> _subscribers = new();

    internal int SubscriberCount => _subscribers.Values.Sum(s => s.Count);

    public void Publish(Tick tick)
    {
        // Für die Live-Anzeige zählt der neueste Stand: Ein verspäteter Tick (ältere EventTime) würde den Kurs
        // zurückspringen lassen. Verspätete Ticks einzuordnen ist Sache von History (Kerzen nach Event-Time).
        var latest = _latest.AddOrUpdate(tick.InstrumentId, tick, (_, current) => tick.EventTime >= current.EventTime ? tick : current);
        if (!ReferenceEquals(latest, tick)) return;
        if (_subscribers.TryGetValue(tick.InstrumentId, out var subscribers))
            foreach (var channel in subscribers.Keys)
                channel.Writer.TryWrite(tick);
    }

    public Tick? Latest(Guid instrumentId) => _latest.GetValueOrDefault(instrumentId);

    /// <summary>
    /// Beendet alle offenen Streams sauber. Beim Herunterfahren aufrufen: Sonst hält ASP.NET die SSE-Verbindungen
    /// bis zum Shutdown-Timeout (30 s) offen und bricht sie dann hart ab – und nicht jeder Proxy reicht einen
    /// harten Abbruch an den Browser weiter. Ein sauberes Ende lässt EventSource sofort neu verbinden.
    /// </summary>
    public void CompleteAll()
    {
        foreach (var subscribers in _subscribers.Values)
            foreach (var channel in subscribers.Keys)
                channel.Writer.TryComplete();
    }

    public async IAsyncEnumerable<Tick> Subscribe(Guid instrumentId, [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateBounded<Tick>(new BoundedChannelOptions(BufferPerSubscriber)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        var subscribers = _subscribers.GetOrAdd(instrumentId, _ => new());
        subscribers.TryAdd(channel, 0);
        try
        {
            if (Latest(instrumentId) is { } latest) channel.Writer.TryWrite(latest);
            await foreach (var tick in channel.Reader.ReadAllAsync(ct))
                yield return tick;
        }
        finally
        {
            subscribers.TryRemove(channel, out _);
        }
    }
}
