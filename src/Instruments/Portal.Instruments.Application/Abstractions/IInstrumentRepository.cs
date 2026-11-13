using Portal.Instruments.Domain.Instruments;

namespace Portal.Instruments.Application.Abstractions;

public interface IInstrumentRepository
{
    Task<Instrument?> GetAsync(Guid id, CancellationToken ct);

    Task<bool> IsinExistsAsync(Isin isin, CancellationToken ct);

    void Add(Instrument instrument);
}
