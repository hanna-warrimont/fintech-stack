using System.Globalization;
using Portal.Instruments.Domain.Abstractions;

namespace Portal.Instruments.Domain.Instruments;

/// <summary>Währung als ISO-4217-Code. Die gültigen Codes liefert .NET selbst (RegionInfo) – keine eigene Liste.</summary>
public sealed record Currency
{
    private static readonly HashSet<string> Known = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Select(c => new RegionInfo(c.Name).ISOCurrencySymbol)
        .ToHashSet();

    private Currency(string value) => Value = value;

    public string Value { get; }

    public static Result<Currency> Create(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        return Known.Contains(normalized) ? Result.Success(new Currency(normalized)) : InstrumentErrors.InvalidCurrency;
    }

    public override string ToString() => Value;
}
