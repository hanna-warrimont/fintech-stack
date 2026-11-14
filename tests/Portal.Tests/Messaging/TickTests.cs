using System.Text.Json;
using Portal.Messaging.Contracts;

namespace Portal.Tests.Messaging;

public sealed class TickTests
{
    [Fact]
    public void Valid_tick_is_parsed()
    {
        var tick = new Tick(Guid.NewGuid(), 7, 101.5m, 3, DateTimeOffset.UtcNow);

        Assert.Equal(tick, Tick.TryParse(JsonSerializer.Serialize(tick, JsonSerializerOptions.Web)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("kein json")]
    [InlineData("{}")]
    [InlineData("""{"instrumentId":"6f1c2a43-8f0e-4a39-9d5e-1b2c3d4e5f60","seq":1}""")]
    public void Invalid_ticks_are_rejected(string? value) => Assert.Null(Tick.TryParse(value));
}
