using Portal.Simulator;

namespace Portal.Tests.Simulator;

public sealed class TickPlanTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Normal_tick_increments_seq()
    {
        long seq = 100;

        var tick = Assert.Single(TickPlan.For(A, 50m, 3, Now, ref seq, new ChaosSettings()));

        Assert.Equal(101, tick.Seq);
        Assert.Equal(101, seq);
        Assert.Equal(Now, tick.EventTime);
    }

    [Fact]
    public void Duplicates_repeat_the_same_tick()
    {
        long seq = 100;

        var ticks = TickPlan.For(A, 50m, 3, Now, ref seq, new ChaosSettings { Duplicates = true });

        Assert.Equal(2, ticks.Count);
        Assert.Equal(ticks[0], ticks[1]);
        Assert.Equal(101, seq);
    }

    [Fact]
    public void Late_adds_old_event_time_tick_every_tenth()
    {
        var chaos = new ChaosSettings { Late = true };
        long seq = 99;

        var ticks = TickPlan.For(A, 50m, 3, Now, ref seq, chaos);

        Assert.Equal(2, ticks.Count);
        Assert.Equal(101, ticks[1].Seq);
        Assert.Equal(Now.AddMinutes(-2), ticks[1].EventTime);

        seq = 100;
        Assert.Single(TickPlan.For(A, 50m, 3, Now, ref seq, chaos));
    }
}
