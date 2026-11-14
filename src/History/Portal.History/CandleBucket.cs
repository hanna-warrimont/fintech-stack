namespace Portal.History;

/// <summary>Die Minute, zu der ein Tick gehört: seine Event-Time in UTC, auf die Minute abgeschnitten.</summary>
public static class CandleBucket
{
    public static DateTimeOffset Of(DateTimeOffset eventTime)
    {
        var utc = eventTime.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMinute, TimeSpan.Zero);
    }
}
