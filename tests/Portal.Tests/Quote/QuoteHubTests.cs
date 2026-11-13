using Portal.Messaging.Contracts;
using Portal.Quote;

namespace Portal.Tests.Quote;

public sealed class QuoteHubTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    private static Tick T(Guid id, long seq) => new(id, seq, 100m + seq, 1, DateTimeOffset.UtcNow);

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

        Assert.True(read.Count <= 17, $"{read.Count} gelesen");
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
}
