namespace Portal.Tests;

/// <summary>Steuerbare Uhr für Tests (statt eines Zusatzpakets).</summary>
public sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
