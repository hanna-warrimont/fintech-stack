using Portal.Instruments.Domain.Abstractions;
using Portal.Instruments.Domain.Instruments;

namespace Portal.Tests.Instruments;

public sealed class InstrumentTests
{
    private static Result<Instrument> Create(string name = "SAP SE") =>
        Instrument.Create(Isin.Create("DE0007164600").Value, Wkn.Create("716460").Value, name, InstrumentType.Share, Currency.Create("EUR").Value);

    [Fact]
    public void Create_starts_active_with_revision_1_and_raises_created()
    {
        var instrument = Create().Value;

        Assert.Equal(InstrumentStatus.Active, instrument.Status);
        Assert.Equal(1, instrument.Revision);
        Assert.NotEqual(Guid.Empty, instrument.Id);
        Assert.Equal([new InstrumentCreated(instrument.Id)], instrument.DomainEvents);
    }

    [Fact]
    public void Create_trims_name() => Assert.Equal("SAP SE", Create("  SAP SE ").Value.Name);

    [Fact]
    public void Create_rejects_blank_or_too_long_name()
    {
        Assert.Equal(InstrumentErrors.NameRequired, Create("  ").Error);
        Assert.Equal(InstrumentErrors.NameTooLong, Create(new string('x', 201)).Error);
    }

    [Fact]
    public void Rename_changes_name_increments_revision_and_raises_renamed()
    {
        var instrument = Create().Value;

        Assert.True(instrument.Rename("Neu").IsSuccess);

        Assert.Equal("Neu", instrument.Name);
        Assert.Equal(2, instrument.Revision);
        Assert.Equal(new InstrumentRenamed(instrument.Id, "Neu"), instrument.DomainEvents[^1]);
    }

    [Fact]
    public void Rename_to_same_name_changes_nothing()
    {
        var instrument = Create().Value;

        Assert.True(instrument.Rename(" SAP SE ").IsSuccess);

        Assert.Equal(1, instrument.Revision);
        Assert.Single(instrument.DomainEvents);
    }

    [Fact]
    public void Rename_rejects_blank_name() => Assert.Equal(InstrumentErrors.NameRequired, Create().Value.Rename(" ").Error);
}
