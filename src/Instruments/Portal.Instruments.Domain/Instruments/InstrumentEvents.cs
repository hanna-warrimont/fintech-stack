using Portal.Instruments.Domain.Abstractions;

namespace Portal.Instruments.Domain.Instruments;

public sealed record InstrumentCreated(Guid InstrumentId) : IDomainEvent;

public sealed record InstrumentRenamed(Guid InstrumentId, string Name) : IDomainEvent;

public sealed record InstrumentIsinChanged(Guid InstrumentId, string Isin) : IDomainEvent;

public sealed record InstrumentDelisted(Guid InstrumentId) : IDomainEvent;
