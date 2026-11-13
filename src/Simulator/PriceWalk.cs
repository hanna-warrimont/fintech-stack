namespace Portal.Simulator;

/// <summary>Zufallspfad: pro Schritt gleichverteilt −0,5 % bis +0,5 %, auf Cent gerundet.</summary>
public static class PriceWalk
{
    public static decimal Next(decimal current, Random rng)
    {
        var change = (decimal)(rng.NextDouble() - 0.5) * 0.01m;
        return Math.Max(0.01m, Math.Round(current * (1 + change), 2));
    }
}
