using Portal.Instruments.Domain.Abstractions;

namespace Portal.Instruments.Domain.CorporateActions;

/// <summary>Fehlerkatalog der Kapitalmaßnahme.</summary>
public static class CorporateActionErrors
{
    public static readonly Error NotFound = new("CorporateAction.NotFound", "Die Kapitalmaßnahme wurde nicht gefunden.", ErrorType.NotFound);
    public static readonly Error InvalidTransition = new("CorporateAction.InvalidTransition", "Dieser Schritt ist im aktuellen Status nicht möglich.", ErrorType.Conflict);
    public static readonly Error FourEyes = new("CorporateAction.FourEyes", "Wer eine Maßnahme erfasst hat, darf sie nicht freigeben.", ErrorType.Conflict);
    public static readonly Error Duplicate = new("CorporateAction.Duplicate", "Für dieses Instrument gibt es an diesem Stichtag schon eine offene Maßnahme dieser Art.", ErrorType.Conflict);
    public static readonly Error ConcurrentUpdate = new("CorporateAction.ConcurrentUpdate", "Die Maßnahme wurde gerade von jemand anderem geändert. Bitte neu laden.", ErrorType.Conflict);
    public static readonly Error LeadTimeTooShort = new("CorporateAction.LeadTimeTooShort", "Der Stichtag liegt zu nah oder in der Vergangenheit.", ErrorType.Validation);
    public static readonly Error AlreadyEffectiveDue = new("CorporateAction.AlreadyEffectiveDue", "Der Stichtag ist erreicht, die Maßnahme kann nicht mehr storniert werden.", ErrorType.Validation);
    public static readonly Error InstrumentDelisted = new("CorporateAction.InstrumentDelisted", "Für ein delistetes Instrument ist keine Maßnahme möglich.", ErrorType.Validation);
    public static readonly Error SameIsin = new("CorporateAction.SameIsin", "Die neue ISIN entspricht der aktuellen.", ErrorType.Validation);
    public static readonly Error InvalidSplitRatio = new("CorporateAction.InvalidSplitRatio", "Das Split-Verhältnis braucht zwei verschiedene Zahlen zwischen 1 und 1000.", ErrorType.Validation);
}
