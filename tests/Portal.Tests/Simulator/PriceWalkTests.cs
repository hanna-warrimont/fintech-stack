using Portal.Simulator;

namespace Portal.Tests.Simulator;

public sealed class PriceWalkTests
{
    [Fact]
    public void Walk_stays_positive_and_moves_at_most_half_percent()
    {
        var rng = new Random(42);
        var current = 240.00m;

        for (var i = 0; i < 10_000; i++)
        {
            var next = PriceWalk.Next(current, rng);

            Assert.True(next > 0, $"Schritt {i}: {next} ist nicht positiv");
            Assert.True(Math.Abs(next - current) <= current * 0.005m + 0.01m, $"Schritt {i}: {current} → {next} zu weit");
            Assert.Equal(Math.Round(next, 2), next);
            current = next;
        }
    }
}
