using Portal.Instruments.Domain.Abstractions;

namespace Portal.Instruments.Domain.Instruments;

public sealed record InstrumentCreated(Guid InstrumentId) : IDomainEvent;

public sealed record InstrumentRenamed(Guid InstrumentId, string Name) : IDomainEvent;
