using Portal.Instruments.Domain.Abstractions;

namespace Portal.Instruments.Domain.CorporateActions;

// Nur diese vier Fakten gehen nach Kafka. Einreichen, Zurückziehen und Verwerfen sind interne Arbeitsschritte.
public sealed record CorporateActionApproved(Guid CorporateActionId, Guid InstrumentId) : IDomainEvent;

public sealed record CorporateActionRejected(Guid CorporateActionId, Guid InstrumentId) : IDomainEvent;

public sealed record CorporateActionCancelled(Guid CorporateActionId, Guid InstrumentId) : IDomainEvent;

public sealed record CorporateActionBecameEffective(Guid CorporateActionId, Guid InstrumentId) : IDomainEvent;
