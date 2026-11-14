using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Portal.Instruments.Application;
using Portal.Instruments.Application.Abstractions;
using Portal.Instruments.Application.Instruments;
using Portal.Instruments.Domain.CorporateActions;
using Portal.Instruments.Domain.Instruments;
using Portal.Instruments.Infrastructure;
using Portal.Messaging;
using Portal.Messaging.Contracts;

namespace Portal.Tests.Instruments;

[Collection("infra")]
public sealed class CorporateActionPersistenceTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly DateTimeOffset Due = Now.AddDays(1);

    [Fact]
    public async Task Approved_action_writes_event_to_outbox_in_same_transaction()
    {
        await using var sp = await Services();
        var id = await NewSplit(sp, Due);

        await Change(sp, id, a => a.Submit());
        await Change(sp, id, a => a.Approve("fred", Now, TimeSpan.FromMinutes(1)));

        await using var db = Data(sp);
        var row = Assert.Single(await Outbox(db, Topics.CorporateActions));
        Assert.Equal("CorporateActionApproved", row.Type);
        var envelope = CorporateActionEvent.TryParse(row.Payload);
        Assert.NotNull(envelope);
        Assert.Equal(row.Key, envelope.Payload.InstrumentId.ToString("D"));
        Assert.Equal(id, envelope.Payload.CorporateActionId);
        Assert.Equal(("Split", 1, 4), (envelope.Payload.Kind, envelope.Payload.SplitOld, envelope.Payload.SplitNew));
    }

    [Fact]
    public async Task Submit_writes_no_outbox_row()
    {
        await using var sp = await Services();
        var id = await NewSplit(sp, Due);

        await Change(sp, id, a => a.Submit());

        await using var db = Data(sp);
        Assert.Empty(await Outbox(db, Topics.CorporateActions));
    }

    [Fact]
    public async Task Second_open_action_same_type_same_day_is_duplicate()
    {
        await using var sp = await Services();
        var instrumentId = await NewInstrument(sp);
        await NewSplit(sp, Due, instrumentId);

        await Assert.ThrowsAsync<DuplicateKeyException>(() => NewSplit(sp, Due.AddMinutes(1), instrumentId));
        await NewSplit(sp, Due.AddDays(1), instrumentId); // anderer Tag
        await using var scope = sp.CreateAsyncScope();
        var instrument = (await scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().GetAsync(instrumentId, default))!;
        scope.ServiceProvider.GetRequiredService<ICorporateActionRepository>()
            .Add(CorporateAction.CreateDelisting(instrument, Due, "pia").Value); // andere Art
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(default);
    }

    [Fact]
    public async Task Concurrent_approvals_one_wins_other_conflicts()
    {
        await using var sp = await Services();
        var id = await NewSplit(sp, Due);
        await Change(sp, id, a => a.Submit());

        await using var first = sp.CreateAsyncScope();
        await using var second = sp.CreateAsyncScope();
        var a1 = (await first.ServiceProvider.GetRequiredService<ICorporateActionRepository>().GetAsync(id, default))!;
        var a2 = (await second.ServiceProvider.GetRequiredService<ICorporateActionRepository>().GetAsync(id, default))!;
        a1.Approve("fred", Now, TimeSpan.FromMinutes(1));
        a2.Approve("frieda", Now, TimeSpan.FromMinutes(1));

        await first.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(default);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            second.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(default));
    }

    [Fact]
    public void Event_contract_rejects_unknown_type()
    {
        var payload = new CorporateActionEvent(Guid.NewGuid(), Guid.NewGuid(), "Split", 1, 4, null, Due);
        var envelope = new Envelope<CorporateActionEvent>(Guid.NewGuid(), "Other", 1, Now, payload);

        Assert.Null(CorporateActionEvent.TryParse(System.Text.Json.JsonSerializer.Serialize(envelope, System.Text.Json.JsonSerializerOptions.Web)));
        Assert.NotNull(CorporateActionEvent.TryParse(System.Text.Json.JsonSerializer.Serialize(
            envelope with { Type = "CorporateActionBecameEffective" }, System.Text.Json.JsonSerializerOptions.Web)));
    }

    private async Task<ServiceProvider> Services()
    {
        var sp = new ServiceCollection()
            .AddInstrumentApplication()
            .AddInstrumentInfrastructure(await postgres.CreateDatabaseAsync())
            .BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InstrumentDbContext>().Database.MigrateAsync();
        return sp;
    }

    private static async Task<Guid> NewInstrument(IServiceProvider sp)
    {
        await using var scope = sp.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<CreateInstrumentHandler>()
            .Handle(new CreateInstrumentCommand("DE0007164600", null, "SAP SE", InstrumentType.Share, "EUR"), default)).Value;
    }

    private static async Task<Guid> NewSplit(IServiceProvider sp, DateTimeOffset effectiveAt, Guid? instrumentId = null)
    {
        instrumentId ??= await NewInstrument(sp);
        await using var scope = sp.CreateAsyncScope();
        var instrument = (await scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().GetAsync(instrumentId.Value, default))!;
        var action = CorporateAction.CreateSplit(instrument, SplitRatio.Create(1, 4).Value, effectiveAt, "pia").Value;
        scope.ServiceProvider.GetRequiredService<ICorporateActionRepository>().Add(action);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(default);
        return action.Id;
    }

    private static async Task Change(IServiceProvider sp, Guid id, Func<CorporateAction, Portal.Instruments.Domain.Abstractions.Result> step)
    {
        await using var scope = sp.CreateAsyncScope();
        var action = (await scope.ServiceProvider.GetRequiredService<ICorporateActionRepository>().GetAsync(id, default))!;
        Assert.True(step(action).IsSuccess);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(default);
    }

    private static NpgsqlConnection Data(IServiceProvider sp) => sp.GetRequiredService<NpgsqlDataSource>().CreateConnection();

    private static async Task<List<OutboxRow>> Outbox(NpgsqlConnection db, string topic) =>
        (await db.QueryAsync<OutboxRow>("SELECT key, type, payload::text AS payload FROM outbox_messages WHERE topic = @topic", new { topic }))
        .ToList();

    private sealed record OutboxRow(string Key, string Type, string Payload);
}
