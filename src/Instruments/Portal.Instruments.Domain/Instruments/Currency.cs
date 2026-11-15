using Portal.Instruments.Domain.Abstractions;

namespace Portal.Instruments.Domain.Instruments;

/// <summary>
/// Währung als ISO-4217-Code. Die gültigen Codes stehen hier fest im Code, statt sie aus den ICU-Daten des Rechners
/// (<c>CultureInfo</c>/<c>RegionInfo</c>) zu lesen: In einem Container ohne ICU oder mit <c>InvariantGlobalization</c>
/// wäre sonst jede Währung ungültig – eine Domänenregel darf nicht vom Betriebssystem-Image abhängen.
/// </summary>
public sealed record Currency
{
    // ISO 4217 Liste 1 (SIX, veröffentlicht 2026-09-17): alle aktiven Währungen. Ohne Fonds-Codes (BOV, CHE, …) und ohne
    // X-Codes für Edelmetalle, Rechen- und Testeinheiten (XAU, XDR, XTS, …); die Währungsunionen XAF, XCD, XCG, XOF, XPF
    // sind dabei. Ändert ISO die Liste (z. B. eine neue Währung), wird sie hier nachgetragen.
    private static readonly HashSet<string> Known = """
        AED AFN ALL AMD AOA ARS AUD AWG AZN BAM BBD BDT BHD BIF BMD BND BOB BRL BSD BTN BWP BYN BZD CAD CDF CHF CLP CNY COP
        CRC CUP CVE CZK DJF DKK DOP DZD EGP ERN ETB EUR FJD FKP GBP GEL GHS GIP GMD GNF GTQ GYD HKD HNL HTG HUF IDR ILS INR
        IQD IRR ISK JMD JOD JPY KES KGS KHR KMF KPW KRW KWD KYD KZT LAK LBP LKR LRD LSL LYD MAD MDL MGA MKD MMK MNT MOP MRU
        MUR MVR MWK MXN MYR MZN NAD NGN NIO NOK NPR NZD OMR PAB PEN PGK PHP PKR PLN PYG QAR RON RSD RUB RWF SAR SBD SCR SDG
        SEK SGD SHP SLE SOS SRD SSP STN SVC SYP SZL THB TJS TMT TND TOP TRY TTD TWD TZS UAH UGX USD UYU UZS VED VES VND VUV
        WST XAF XCD XCG XOF XPF YER ZAR ZMW ZWG
        """.Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

    private Currency(string value) => Value = value;

    public string Value { get; }

    public static Result<Currency> Create(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        return Known.Contains(normalized) ? Result.Success(new Currency(normalized)) : InstrumentErrors.InvalidCurrency;
    }

    public override string ToString() => Value;
}
