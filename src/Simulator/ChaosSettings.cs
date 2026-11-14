namespace Portal.Simulator;

/// <summary>Zustand der Störschalter. Wird von <see cref="ChaosEndpoints"/> gesetzt und vom Worker pro Takt gelesen.</summary>
public sealed class ChaosSettings
{
    public volatile bool Duplicates;
    public volatile bool Late;
    public volatile bool Paused;

    // ponytail: double ohne Lock – ein Takt mit der alten Rate ist egal
    public double TicksPerSecondPerInstrument { get; set; } = 2;
}
