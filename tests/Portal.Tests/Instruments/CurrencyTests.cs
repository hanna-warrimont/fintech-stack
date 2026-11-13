using Portal.Instruments.Domain.Instruments;

namespace Portal.Tests.Instruments;

public sealed class CurrencyTests
{
    [Fact]
    public void Eur_is_accepted() => Assert.Equal("EUR", Currency.Create("EUR").Value.Value);

    [Fact]
    public void Input_is_trimmed_and_uppercased() => Assert.Equal("USD", Currency.Create(" usd ").Value.Value);

    [Theory]
    [InlineData("XYZ")]
    [InlineData("EU")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_codes_are_rejected(string? value) =>
        Assert.Equal(InstrumentErrors.InvalidCurrency, Currency.Create(value).Error);
}
