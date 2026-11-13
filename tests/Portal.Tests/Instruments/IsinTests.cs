using Portal.Instruments.Domain.Instruments;

namespace Portal.Tests.Instruments;

public sealed class IsinTests
{
    [Theory]
    [InlineData("DE0007164600")]
    [InlineData("US0378331005")]
    [InlineData("IE00B4L5Y983")]
    public void Valid_isins_are_accepted(string value) => Assert.Equal(value, Isin.Create(value).Value.Value);

    [Fact]
    public void Input_is_trimmed_and_uppercased() => Assert.Equal("DE0007164600", Isin.Create(" de0007164600 ").Value.Value);

    [Fact]
    public void Wrong_check_digit_is_rejected() => Assert.Equal(InstrumentErrors.InvalidIsin, Isin.Create("DE0007164601").Error);

    [Theory]
    [InlineData("DE000716460")]
    [InlineData("DE00071646000")]
    [InlineData("DE0007164-00")]
    [InlineData("")]
    [InlineData(null)]
    public void Wrong_length_or_characters_are_rejected(string? value) =>
        Assert.Equal(InstrumentErrors.InvalidIsin, Isin.Create(value).Error);
}
