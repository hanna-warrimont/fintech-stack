using Portal.Instruments.Domain.Abstractions;

namespace Portal.Instruments.Domain.CorporateActions;

/// <summary>Split-Verhältnis alt:neu. 1:4 = aus 1 Aktie werden 4; ein Reverse-Split ist z. B. 4:1.</summary>
public sealed record SplitRatio
{
    private SplitRatio(int old, int @new) => (Old, New) = (old, @new);

    public int Old { get; }
    public int New { get; }

    /// <summary>Faktor für Kurse vor dem Split: 1:4 → 0,25.</summary>
    public decimal Factor => (decimal)Old / New;

    public static Result<SplitRatio> Create(int old, int @new) =>
        old is < 1 or > 1000 || @new is < 1 or > 1000 || old == @new
            ? CorporateActionErrors.InvalidSplitRatio
            : new SplitRatio(old, @new);
}
