using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Portal.Instruments.Application.Abstractions;
using Portal.Instruments.Domain.Abstractions;
using Portal.Instruments.Domain.CorporateActions;
using Portal.Instruments.Domain.Instruments;
using Portal.Instruments.Infrastructure.Outbox;

namespace Portal.Instruments.Infrastructure;

public sealed class InstrumentDbContext(DbContextOptions<InstrumentDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<CorporateAction> CorporateActions => Set<CorporateAction>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InstrumentDbContext).Assembly);

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken ct) => await SaveChangesAsync(ct);

    /// <summary>
    /// Transactional Outbox: Die Integration Events landen in derselben Transaktion wie die fachliche Änderung.
    /// Entweder beides wird gespeichert oder nichts – nie „gespeichert, aber nicht veröffentlicht“.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        var changed = ChangeTracker.Entries<Entity>().Select(e => e.Entity).Where(e => e.DomainEvents.Count > 0).ToList();
        var now = DateTimeOffset.UtcNow;
        var traceParent = Activity.Current?.Id;
        foreach (var entity in changed)
            OutboxMessages.AddRange(entity switch
            {
                Instrument instrument => IntegrationEventMapper.ToOutbox(instrument, now, traceParent) is { } snapshot ? [snapshot] : [],
                CorporateAction action => IntegrationEventMapper.ToOutbox(action, now, traceParent),
                _ => [],
            });

        try
        {
            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
            changed.ForEach(i => i.ClearDomainEvents());
            return result;
        }
        catch (DbUpdateConcurrencyException e)
        {
            throw new ConcurrencyConflictException(e);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new DuplicateKeyException(e);
        }
    }
}
