using Microsoft.EntityFrameworkCore;
using Portal.Instruments.Application.Abstractions;
using Portal.Instruments.Domain.Instruments;

namespace Portal.Instruments.Infrastructure;

internal sealed class InstrumentRepository(InstrumentDbContext db) : IInstrumentRepository
{
    public Task<Instrument?> GetAsync(Guid id, CancellationToken ct) => db.Instruments.FirstOrDefaultAsync(i => i.Id == id, ct);

    public Task<bool> IsinExistsAsync(Isin isin, CancellationToken ct) => db.Instruments.AnyAsync(i => i.Isin == isin, ct);

    public void Add(Instrument instrument) => db.Instruments.Add(instrument);
}
