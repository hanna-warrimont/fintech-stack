using Portal.Instruments.Domain.CorporateActions;

namespace Portal.Instruments.Application.Abstractions;

public interface ICorporateActionRepository
{
    Task<CorporateAction?> GetAsync(Guid id, CancellationToken ct);

    void Add(CorporateAction action);
}
