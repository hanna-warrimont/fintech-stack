using System.Text.RegularExpressions;
using Portal.Instruments.Domain.Abstractions;

namespace Portal.Instruments.Domain.Instruments;

/// <summary>
/// ISIN nach ISO 6166: 2 Buchstaben Land, 9 Zeichen nationale Kennung, 1 Prüfziffer.
/// Value Object: unveränderlich, nur gültig erzeugbar – wer eine <see cref="Isin"/> hat, hat eine gültige.
/// </summary>
public sealed partial record Isin
{
    private Isin(string value) => Value = value;

    public string Value { get; }

    public static Result<Isin> Create(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        return Shape().IsMatch(normalized) && HasValidCheckDigit(normalized)
            ? Result.Success(new Isin(normalized))
            : InstrumentErrors.InvalidIsin;
    }

    // Luhn über die Ziffernfolge; Buchstaben zählen als A=10 … Z=35.
    private static bool HasValidCheckDigit(string value)
    {
        var digits = string.Concat(value[..11].Select(c => char.IsDigit(c) ? $"{c - '0'}" : $"{c - 'A' + 10}"));
        var sum = 0;
        for (int i = digits.Length - 1, doubleIt = 1; i >= 0; i--, doubleIt ^= 1)
        {
            var d = (digits[i] - '0') * (doubleIt == 1 ? 2 : 1);
            sum += d > 9 ? d - 9 : d;
        }

        return (10 - sum % 10) % 10 == value[11] - '0';
    }

    [GeneratedRegex("^[A-Z]{2}[A-Z0-9]{9}[0-9]$")]
    private static partial Regex Shape();

    public override string ToString() => Value;
}
