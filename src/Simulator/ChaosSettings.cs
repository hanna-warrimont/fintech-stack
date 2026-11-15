namespace Portal.Simulator;

/// <summary>Zustand der Störschalter. Wird von <see cref="ChaosEndpoints"/> gesetzt und vom Worker pro Takt gelesen.</summary>
public sealed class ChaosSettings
{
    // Bewusst Felder: volatile gibt es nur für Felder, und der Worker soll jede Änderung sofort sehen.
#pragma warning disable CA1051
    public volatile bool Duplicates;
    public volatile bool Late;
    public volatile bool Paused;
#pragma warning restore CA1051

    // ponytail: double ohne Lock – ein Takt mit der alten Rate ist egal
    public double TicksPerSecondPerInstrument { get; set; } = 2;
}
