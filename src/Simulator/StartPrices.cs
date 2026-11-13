namespace Portal.Simulator;

/// <summary>Plausible Startkurse für bekannte ISINs, damit die Charts nicht alle bei 100 beginnen.</summary>
public static class StartPrices
{
    private static readonly Dictionary<string, decimal> Known = new()
    {
        ["DE0007164600"] = 240.00m, // SAP
        ["DE0007236101"] = 190.00m, // Siemens
        ["DE0008404005"] = 340.00m, // Allianz
        ["IE00B4L5Y983"] = 105.00m, // MSCI World
    };

    public static decimal For(string isin) => Known.GetValueOrDefault(isin, 100.00m);
}
