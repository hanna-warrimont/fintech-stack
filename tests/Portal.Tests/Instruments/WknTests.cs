using Portal.Instruments.Domain.Instruments;

namespace Portal.Tests.Instruments;

public sealed class WknTests
{
    [Theory]
    [InlineData("716460")]
    [InlineData("A0RPWH")]
    public void Valid_wkns_are_accepted(string value) => Assert.Equal(value, Wkn.Create(value).Value.Value);

    [Fact]
    public void Input_is_trimmed_and_uppercased() => Assert.Equal("A0RPWH", Wkn.Create(" a0rpwh ").Value.Value);

    [Theory]
    [InlineData("71646")]
    [InlineData("716460X")]
    [InlineData("71646!")]
    public void Invalid_wkns_are_rejected(string value) => Assert.Equal(InstrumentErrors.InvalidWkn, Wkn.Create(value).Error);
}
