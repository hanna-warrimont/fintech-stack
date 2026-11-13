using Portal.Instruments.Domain.Abstractions;

namespace Portal.Instruments.Domain.Instruments;

/// <summary>Fehlerkatalog des Instrument-Aggregats: alle erwartbaren Fehler an einer Stelle.</summary>
public static class InstrumentErrors
{
    public static readonly Error InvalidIsin = new("Instrument.InvalidIsin", "Die ISIN ist ungültig.", ErrorType.Validation);
    public static readonly Error InvalidWkn = new("Instrument.InvalidWkn", "Die WKN muss aus 6 Buchstaben oder Ziffern bestehen.", ErrorType.Validation);
    public static readonly Error InvalidCurrency = new("Instrument.InvalidCurrency", "Die Währung ist kein gültiger ISO-4217-Code.", ErrorType.Validation);
    public static readonly Error NameRequired = new("Instrument.NameRequired", "Der Name darf nicht leer sein.", ErrorType.Validation);
    public static readonly Error NameTooLong = new("Instrument.NameTooLong", $"Der Name darf höchstens {Instrument.MaxNameLength} Zeichen haben.", ErrorType.Validation);
    public static readonly Error NotFound = new("Instrument.NotFound", "Das Instrument wurde nicht gefunden.", ErrorType.NotFound);
    public static readonly Error DuplicateIsin = new("Instrument.DuplicateIsin", "Ein Instrument mit dieser ISIN existiert bereits.", ErrorType.Conflict);
}
