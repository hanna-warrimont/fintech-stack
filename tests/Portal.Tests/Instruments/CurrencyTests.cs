using Portal.Instruments.Domain.Instruments;

namespace Portal.Tests.Instruments;

public sealed class CurrencyTests
{
    [Fact]
    public void Eur_is_accepted() => Assert.Equal("EUR", Currency.Create("EUR").Value.Value);

    [Fact]
    public void Input_is_trimmed_and_uppercased() => Assert.Equal("USD", Currency.Create(" usd ").Value.Value);

    [Theory]
    [InlineData("JPY")]
    [InlineData("CHF")]
    [InlineData("XOF")] // Währungsunion (CFA-Franc)
    [InlineData("ZWG")] // seit 2024
    public void Active_iso_codes_are_accepted(string value) => Assert.True(Currency.Create(value).IsSuccess);

    [Theory]
    [InlineData("XYZ")]
    [InlineData("EU")]
    [InlineData("XAU")] // Gold, keine Währung
    [InlineData("XTS")] // für Tests reserviert
    [InlineData("CHE")] // Fonds-Code
    [InlineData("HRK")] // seit 2023 durch EUR ersetzt
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_codes_are_rejected(string? value) =>
        Assert.Equal(InstrumentErrors.InvalidCurrency, Currency.Create(value).Error);
}
