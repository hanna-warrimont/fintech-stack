using System.Text.RegularExpressions;
using Portal.Instruments.Domain.Abstractions;

namespace Portal.Instruments.Domain.Instruments;

/// <summary>Wertpapierkennnummer: 6 Buchstaben oder Ziffern (deutsche Kennung, optional am Instrument).</summary>
public sealed partial record Wkn
{
    private Wkn(string value) => Value = value;

    public string Value { get; }

    public static Result<Wkn> Create(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        return Shape().IsMatch(normalized) ? Result.Success(new Wkn(normalized)) : InstrumentErrors.InvalidWkn;
    }

    [GeneratedRegex("^[A-Z0-9]{6}$")]
    private static partial Regex Shape();

    public override string ToString() => Value;
}
