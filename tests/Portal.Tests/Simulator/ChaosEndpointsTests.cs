using Portal.Simulator;

namespace Portal.Tests.Simulator;

public sealed class ChaosEndpointsTests
{
    [Theory]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    [InlineData(0.05, false)]
    [InlineData(1001, false)]
    [InlineData(0.1, true)]
    [InlineData(1000, true)]
    public void Rate_is_validated(double ticksPerSecond, bool valid) =>
        Assert.Equal(valid, ChaosEndpoints.IsValidRate(ticksPerSecond));
}
