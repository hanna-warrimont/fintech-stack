using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Instruments.Domain.Instruments;

namespace Portal.Instruments.Infrastructure.Configurations;

internal sealed class InstrumentConfiguration : IEntityTypeConfiguration<Instrument>
{
    public void Configure(EntityTypeBuilder<Instrument> b)
    {
        b.ToTable("instruments");
        b.HasKey(i => i.Id);
        b.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();
        // Value Objects werden als ihr Wert gespeichert und beim Laden wieder über Create(...) gebaut.
        b.Property(i => i.Isin).HasColumnName("isin").HasMaxLength(12)
            .HasConversion(v => v.Value, v => Isin.Create(v).Value);
        b.HasIndex(i => i.Isin).IsUnique(); // entscheidet den Wettlauf zweier gleichzeitiger Anlagen
        b.Property(i => i.Wkn).HasColumnName("wkn").HasMaxLength(6)
            .HasConversion(v => v!.Value, v => Wkn.Create(v).Value);
        b.Property(i => i.Name).HasColumnName("name").HasMaxLength(Instrument.MaxNameLength);
        b.Property(i => i.Type).HasColumnName("type").HasConversion<string>();
        b.Property(i => i.Currency).HasColumnName("currency").HasMaxLength(3)
            .HasConversion(v => v.Value, v => Currency.Create(v).Value);
        b.Property(i => i.Status).HasColumnName("status").HasConversion<string>();
        b.Property(i => i.Revision).HasColumnName("revision");
        b.Ignore(i => i.DomainEvents);
    }
}
