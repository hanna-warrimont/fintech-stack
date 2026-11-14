using Portal.Instruments.Domain.Abstractions;

namespace Portal.Instruments.Domain.Instruments;

/// <summary>
/// Aggregat-Wurzel für Stammdaten eines Finanzinstruments. Zustand ändert sich nur über Methoden,
/// die Regeln prüfen und Domain Events auslösen. <see cref="Revision"/> zählt jede Änderung.
/// </summary>
public sealed class Instrument : Entity
{
    public const int MaxNameLength = 200;

    private Instrument() { } // für EF

    public Guid Id { get; private set; }
    public Isin Isin { get; private set; } = null!;
    public Wkn? Wkn { get; private set; }
    public string Name { get; private set; } = null!;
    public InstrumentType Type { get; private set; }
    public Currency Currency { get; private set; } = null!;
    public InstrumentStatus Status { get; private set; }
    public int Revision { get; private set; }

    public static Result<Instrument> Create(Isin isin, Wkn? wkn, string name, InstrumentType type, Currency currency)
    {
        if (ValidateName(name) is { } error) return error;

        var instrument = new Instrument
        {
            Id = Guid.CreateVersion7(),
            Isin = isin,
            Wkn = wkn,
            Name = name.Trim(),
            Type = type,
            Currency = currency,
            Status = InstrumentStatus.Active,
            Revision = 1,
        };
        instrument.Raise(new InstrumentCreated(instrument.Id));
        return instrument;
    }

    public Result Rename(string name)
    {
        if (ValidateName(name) is { } error) return Result.Failure(error);
        if (name.Trim() == Name) return Result.Success();

        Name = name.Trim();
        Revision++;
        Raise(new InstrumentRenamed(Id, Name));
        return Result.Success();
    }

    /// <summary>Folge eines wirksamen ISIN-Wechsels. Die <see cref="Id"/> bleibt – darum ist sie der Kafka-Key, nicht die ISIN.</summary>
    public Result ChangeIsin(Isin isin)
    {
        if (isin == Isin) return Result.Success();

        Isin = isin;
        Revision++;
        Raise(new InstrumentIsinChanged(Id, isin.Value));
        return Result.Success();
    }

    public Result Delist()
    {
        if (Status == InstrumentStatus.Delisted) return Result.Failure(InstrumentErrors.AlreadyDelisted);

        Status = InstrumentStatus.Delisted;
        Revision++;
        Raise(new InstrumentDelisted(Id));
        return Result.Success();
    }

    private static Error? ValidateName(string? name) => name?.Trim() switch
    {
        null or "" => InstrumentErrors.NameRequired,
        { Length: > MaxNameLength } => InstrumentErrors.NameTooLong,
        _ => null,
    };
}
