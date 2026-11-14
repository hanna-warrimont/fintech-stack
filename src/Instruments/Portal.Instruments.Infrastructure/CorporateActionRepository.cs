using Microsoft.EntityFrameworkCore;
using Portal.Instruments.Application.Abstractions;
using Portal.Instruments.Domain.CorporateActions;

namespace Portal.Instruments.Infrastructure;

internal sealed class CorporateActionRepository(InstrumentDbContext db) : ICorporateActionRepository
{
    public Task<CorporateAction?> GetAsync(Guid id, CancellationToken ct) => db.CorporateActions.FirstOrDefaultAsync(a => a.Id == id, ct);

    public void Add(CorporateAction action) => db.CorporateActions.Add(action);
}
