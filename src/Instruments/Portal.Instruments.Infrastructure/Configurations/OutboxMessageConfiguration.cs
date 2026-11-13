using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Instruments.Infrastructure.Outbox;

namespace Portal.Instruments.Infrastructure.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages");
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(m => m.Topic).HasColumnName("topic");
        b.Property(m => m.Key).HasColumnName("key");
        b.Property(m => m.Type).HasColumnName("type");
        b.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb");
        b.Property(m => m.OccurredAt).HasColumnName("occurred_at");
        b.Property(m => m.TraceParent).HasColumnName("trace_parent");
        b.Property(m => m.ProcessedAt).HasColumnName("processed_at");
        // Der Dispatcher sucht nur unverarbeitete Zeilen – der partielle Index bleibt klein.
        b.HasIndex(m => m.OccurredAt).HasDatabaseName("ix_outbox_unprocessed").HasFilter("processed_at IS NULL");
    }
}
