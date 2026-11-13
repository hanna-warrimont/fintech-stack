using Portal.Instruments.Application.Abstractions;
using Portal.Instruments.Domain.Abstractions;
using Portal.Instruments.Domain.Instruments;

namespace Portal.Instruments.Application.Instruments;

public sealed record RenameInstrumentCommand(Guid Id, string Name);

public sealed class RenameInstrumentHandler(IInstrumentRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<Result> Handle(RenameInstrumentCommand command, CancellationToken ct)
    {
        var instrument = await repository.GetAsync(command.Id, ct);
        if (instrument is null) return Result.Failure(InstrumentErrors.NotFound);

        var result = instrument.Rename(command.Name);
        if (result.IsFailure) return result;

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            return Result.Failure(InstrumentErrors.ConcurrentUpdate);
        }

        return Result.Success();
    }
}
