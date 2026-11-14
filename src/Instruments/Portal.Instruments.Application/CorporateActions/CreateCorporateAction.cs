using System.Diagnostics;
using Portal.Instruments.Application.Abstractions;
using Portal.Instruments.Domain.Abstractions;
using Portal.Instruments.Domain.CorporateActions;
using Portal.Instruments.Domain.Instruments;

namespace Portal.Instruments.Application.CorporateActions;

public sealed record CreateCorporateActionCommand(
    Guid InstrumentId, CorporateActionType Type, int? SplitOld, int? SplitNew, string? NewIsin, DateTimeOffset EffectiveAt, string User);

public sealed class CreateCorporateActionHandler(
    IInstrumentRepository instruments, ICorporateActionRepository actions, IUnitOfWork unitOfWork)
{
    public async Task<Result<Guid>> Handle(CreateCorporateActionCommand command, CancellationToken ct)
    {
        var instrument = await instruments.GetAsync(command.InstrumentId, ct);
        if (instrument is null) return InstrumentErrors.NotFound;

        var action = command.Type switch
        {
            CorporateActionType.Split => SplitRatio.Create(command.SplitOld ?? 0, command.SplitNew ?? 0) is { IsSuccess: true } ratio
                ? CorporateAction.CreateSplit(instrument, ratio.Value, command.EffectiveAt, command.User)
                : CorporateActionErrors.InvalidSplitRatio,
            CorporateActionType.IsinChange => Isin.Create(command.NewIsin ?? "") is { IsSuccess: true } isin
                ? CorporateAction.CreateIsinChange(instrument, isin.Value, command.EffectiveAt, command.User)
                : InstrumentErrors.InvalidIsin,
            CorporateActionType.Delisting => CorporateAction.CreateDelisting(instrument, command.EffectiveAt, command.User),
            _ => CorporateActionErrors.InvalidType, // nie still auf die folgenreichste Art ausweichen
        };
        if (action.IsFailure) return action.Error;
        // Früh abweisen statt erst zum Stichtag scheitern (der Job fängt den Wettlauf trotzdem ab).
        if (action.Value.NewIsin is { } newIsin && await instruments.IsinExistsAsync(newIsin, ct)) return InstrumentErrors.DuplicateIsin;

        actions.Add(action.Value);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DuplicateKeyException)
        {
            return CorporateActionErrors.Duplicate; // partieller Unique-Index: eine offene Maßnahme pro Art und Stichtag
        }

        Activity.Current?.SetTag("corporateActionId", action.Value.Id);
        return action.Value.Id;
    }
}
