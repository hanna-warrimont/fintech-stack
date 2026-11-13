using Portal.Instruments.Application.Abstractions;
using Portal.Instruments.Domain.Abstractions;
using Portal.Instruments.Domain.Instruments;

namespace Portal.Instruments.Application.Instruments;

public sealed record CreateInstrumentCommand(string Isin, string? Wkn, string Name, InstrumentType Type, string Currency);

public sealed class CreateInstrumentHandler(IInstrumentRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<Result<Guid>> Handle(CreateInstrumentCommand command, CancellationToken ct)
    {
        var isin = Isin.Create(command.Isin);
        if (isin.IsFailure) return isin.Error;
        var currency = Currency.Create(command.Currency);
        if (currency.IsFailure) return currency.Error;
        Wkn? wkn = null;
        if (!string.IsNullOrWhiteSpace(command.Wkn))
        {
            var parsed = Wkn.Create(command.Wkn);
            if (parsed.IsFailure) return parsed.Error;
            wkn = parsed.Value;
        }

        if (await repository.IsinExistsAsync(isin.Value, ct)) return InstrumentErrors.DuplicateIsin;

        var instrument = Instrument.Create(isin.Value, wkn, command.Name, command.Type, currency.Value);
        if (instrument.IsFailure) return instrument.Error;

        repository.Add(instrument.Value);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DuplicateKeyException)
        {
            // Wettlauf: Zwei Anfragen haben die Vorabprüfung gleichzeitig bestanden – der Unique-Index entscheidet.
            return InstrumentErrors.DuplicateIsin;
        }

        return instrument.Value.Id;
    }
}
