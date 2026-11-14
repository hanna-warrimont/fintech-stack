using System.Diagnostics;
using Portal.Instruments.Application.Abstractions;
using Portal.Instruments.Domain.Abstractions;
using Portal.Instruments.Domain.CorporateActions;

namespace Portal.Instruments.Application.CorporateActions;

public enum CorporateActionStep { Submit, Withdraw, Discard, Approve, Reject, Cancel }

public sealed record ChangeCorporateActionCommand(Guid Id, CorporateActionStep Step, string User);

/// <summary>Ein Arbeitsschritt am Aggregat. Die Regeln (Vier-Augen, Vorlauf, Zustände) prüft das Aggregat selbst.</summary>
public sealed class ChangeCorporateActionHandler(
    ICorporateActionRepository actions, IUnitOfWork unitOfWork, TimeProvider time, TimeSpan leadTime)
{
    public async Task<Result> Handle(ChangeCorporateActionCommand command, CancellationToken ct)
    {
        // Tag am Request-Span: Trace 1 (Freigabe → Approved) lässt sich im Dashboard über die Maßnahme finden.
        Activity.Current?.SetTag("corporateActionId", command.Id);
        var action = await actions.GetAsync(command.Id, ct);
        if (action is null) return Result.Failure(CorporateActionErrors.NotFound);

        var now = time.GetUtcNow();
        var result = command.Step switch
        {
            CorporateActionStep.Submit => action.Submit(),
            CorporateActionStep.Withdraw => action.Withdraw(),
            CorporateActionStep.Discard => action.Discard(),
            CorporateActionStep.Approve => action.Approve(command.User, now, leadTime),
            CorporateActionStep.Reject => action.Reject(command.User),
            CorporateActionStep.Cancel => action.Cancel(command.User, now),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
        if (result.IsFailure) return result;

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            return Result.Failure(CorporateActionErrors.ConcurrentUpdate);
        }

        return Result.Success();
    }
}
