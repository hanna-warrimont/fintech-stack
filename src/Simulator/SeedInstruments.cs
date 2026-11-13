namespace Portal.Simulator;

public sealed record SeedInstrument(Guid Id, string Isin, string Name, string Currency, decimal StartPrice);

// ponytail: feste Seed-Liste, ersetzt durch instruments.v1 in Runde 2
public static class SeedInstruments
{
    public static readonly IReadOnlyList<SeedInstrument> All =
    [
        new(Guid.Parse("6939a8d3-111d-409b-897b-996093dd32f8"), "DE0007164600", "SAP SE", "EUR", 240.00m),
        new(Guid.Parse("f30331b5-52ba-4753-955e-e2b1ab299093"), "DE0007236101", "Siemens AG", "EUR", 190.00m),
        new(Guid.Parse("624d6f7f-39d4-450f-9ab7-9b174ab0af11"), "DE0008404005", "Allianz SE", "EUR", 340.00m),
        new(Guid.Parse("f07c2bf3-78e1-4160-8672-29510ce2a233"), "IE00B4L5Y983", "iShares Core MSCI World UCITS ETF", "EUR", 105.00m),
    ];
}
