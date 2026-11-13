namespace Portal.Instruments.Domain.Abstractions;

public enum ErrorType { Validation, NotFound, Conflict }

/// <summary>Ein erwartbarer fachlicher Fehler – kein Exception-Fall. Der Code ist stabil, die Meldung für Menschen.</summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Validation);
}
