using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Instruments.Domain.CorporateActions;
using Portal.Instruments.Domain.Instruments;

namespace Portal.Instruments.Infrastructure.Configurations;

internal sealed class CorporateActionConfiguration : IEntityTypeConfiguration<CorporateAction>
{
    public void Configure(EntityTypeBuilder<CorporateAction> b)
    {
        b.ToTable("corporate_actions");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(a => a.InstrumentId).HasColumnName("instrument_id");
        b.HasOne<Instrument>().WithMany().HasForeignKey(a => a.InstrumentId);
        b.Property(a => a.Type).HasColumnName("type").HasConversion<string>();
        b.ComplexProperty(a => a.Ratio, r =>
        {
            r.Property(x => x.Old).HasColumnName("split_old");
            r.Property(x => x.New).HasColumnName("split_new");
        });
        b.Property(a => a.NewIsin).HasColumnName("new_isin").HasMaxLength(12)
            .HasConversion(v => v!.Value, v => Isin.Create(v).Value);
        b.Property(a => a.EffectiveAt).HasColumnName("effective_at");
        b.Property(a => a.Status).HasColumnName("status").HasConversion<string>();
        b.Property(a => a.CreatedBy).HasColumnName("created_by").HasMaxLength(100);
        b.Property(a => a.DecidedBy).HasColumnName("decided_by").HasMaxLength(100);
        // Optimistic Concurrency wie beim Instrument: zwei gleichzeitige Freigaben → der zweite Speichervorgang verliert.
        b.Property(a => a.Revision).HasColumnName("revision").IsConcurrencyToken();
        // Eindeutigkeit „eine offene Maßnahme pro Instrument, Art und Stichtag“: partieller Ausdrucks-Index in der Migration.
        b.Ignore(a => a.DomainEvents);
    }
}
