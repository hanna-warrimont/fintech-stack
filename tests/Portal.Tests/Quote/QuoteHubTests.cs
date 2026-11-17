using Portal.Messaging.Contracts;
using Portal.Quote;

namespace Portal.Tests.Quote;

public sealed class QuoteHubTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    private static Tick T(Guid id, long seq) => new(id, seq, 100m + seq, 1, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Older_event_time_does_not_replace_latest()
    {
        var hub = new QuoteHub();
        var t0 = DateTimeOffset.UtcNow;
        hub.Publish(new Tick(A, 1, 100m, 1, t0));
        await using var e = hub.Subscribe(A, CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await e.MoveNextAsync()); // letzter Stand: seq 1

        hub.Publish(new Tick(A, 2, 90m, 1, t0.AddMinutes(-2))); // verspätet
        Assert.Equal(1, hub.Latest(A)?.Seq);

        hub.Publish(new Tick(A, 3, 101m, 1, t0.AddSeconds(1)));
        Assert.True(await e.MoveNextAsync());
        Assert.Equal(3, e.Current.Seq);
    }

    [Fact]
    public async Task Subscribe_yields_latest_first()
    {
        var hub = new QuoteHub();
        hub.Publish(T(A, 1));

        await using var e = hub.Subscribe(A, CancellationToken.None).GetAsyncEnumerator();

        Assert.True(await e.MoveNextAsync());
        Assert.Equal(1, e.Current.Seq);
    }

    [Fact]
    public async Task Subscribe_receives_only_its_instrument()
    {
        var hub = new QuoteHub();
        await using var e = hub.Subscribe(A, CancellationToken.None).GetAsyncEnumerator();
        var next = e.MoveNextAsync();

        hub.Publish(T(B, 1));
        hub.Publish(T(A, 2));

        Assert.True(await next);
        Assert.Equal(A, e.Current.InstrumentId);
        Assert.Equal(2, e.Current.Seq);
    }

    [Fact]
    public async Task Slow_subscriber_drops_oldest_keeps_newest()
    {
        var hub = new QuoteHub();
        await using var e = hub.Subscribe(A, CancellationToken.None).GetAsyncEnumerator();
        var first = e.MoveNextAsync(); // registriert das Abo, liest aber nicht weiter

        for (var seq = 1; seq <= 100; seq++) hub.Publish(T(A, seq));

        var read = new List<long>();
        Assert.True(await first);
        read.Add(e.Current.Seq);
        while (read[^1] != 100 && await e.MoveNextAsync()) read.Add(e.Current.Seq);

        // Der erste Tick ging an das wartende MoveNext, danach passt nur der Puffer: die neuesten, lückenlos bis 100.
        Assert.True(read.Count <= QuoteHub.BufferPerSubscriber + 1, $"{read.Count} gelesen");
        var buffered = read.Skip(1).ToList();
        Assert.Equal(Enumerable.Range((int)buffered[0], buffered.Count).Select(i => (long)i), buffered);
        Assert.Equal(100, read[^1]);
    }

    [Fact]
    public async Task Subscribe_ends_and_unregisters_on_cancel()
    {
        var hub = new QuoteHub();
        using var cts = new CancellationTokenSource();
        var e = hub.Subscribe(A, cts.Token).GetAsyncEnumerator();
        var pending = e.MoveNextAsync();
        Assert.Equal(1, hub.SubscriberCount);

        await cts.CancelAsync();
        try { await pending; } catch (OperationCanceledException) { }
        await e.DisposeAsync();

        Assert.Equal(0, hub.SubscriberCount);
    }

    [Fact]
    public void Latest_is_null_for_unknown_instrument() => Assert.Null(new QuoteHub().Latest(A));

    [Fact]
    public async Task CompleteAll_ends_open_subscriptions()
    {
        var hub = new QuoteHub();
        await using var e = hub.Subscribe(A, CancellationToken.None).GetAsyncEnumerator();
        var pending = e.MoveNextAsync();

        hub.CompleteAll(); // beim Herunterfahren: Streams sauber beenden, damit Clients sofort neu verbinden

        Assert.False(await pending.AsTask().WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(0, hub.SubscriberCount);
    }
}
