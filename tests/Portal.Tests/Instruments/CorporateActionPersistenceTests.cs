using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Portal.Instruments.Application;
using Portal.Instruments.Application.Abstractions;
using Portal.Instruments.Application.CorporateActions;
using Portal.Instruments.Application.Instruments;
using Portal.Instruments.Domain.CorporateActions;
using Portal.Instruments.Domain.Instruments;
using Portal.Instruments.Infrastructure;
using Portal.Messaging;
using Portal.Messaging.Contracts;
using Portal.Instruments.Domain.Abstractions;
using static Portal.Tests.Instruments.Users;

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
        await Change(sp, id, a => a.Approve(Fred, Now, TimeSpan.FromMinutes(1)));

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
            .Add(CorporateAction.CreateDelisting(instrument, Due, Pia).Value); // andere Art
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
        a1.Approve(Fred, Now, TimeSpan.FromMinutes(1));
        a2.Approve(Frieda, Now, TimeSpan.FromMinutes(1));

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

    [Fact]
    public async Task Create_and_full_approval_via_handlers()
    {
        var time = new FixedTime(Now);
        await using var sp = await Services(time);
        var instrumentId = await NewInstrument(sp);

        var id = (await Create(sp, new CreateCorporateActionCommand(instrumentId, CorporateActionType.Split, 1, 4, null, Due, Pia))).Value;
        Assert.True((await Step(sp, id, CorporateActionStep.Submit, Pia)).IsSuccess);
        Assert.True((await Step(sp, id, CorporateActionStep.Approve, Fred)).IsSuccess);

        var dto = Assert.Single(await sp.GetRequiredService<ListCorporateActionsQuery>().Worklist(default));
        Assert.Equal(("Approved", "pia", "fred", "SAP SE"), (dto.Status, dto.CreatedBy, dto.DecidedBy, dto.InstrumentName));
    }

    [Fact]
    public async Task Approve_by_creator_via_handler_returns_four_eyes_error()
    {
        await using var sp = await Services(new FixedTime(Now));
        var id = (await Create(sp, new CreateCorporateActionCommand(await NewInstrument(sp), CorporateActionType.Delisting, null, null, null, Due, Frieda))).Value;
        await Step(sp, id, CorporateActionStep.Submit, Frieda);

        Assert.Equal(CorporateActionErrors.FourEyes, (await Step(sp, id, CorporateActionStep.Approve, Frieda)).Error);
    }

    [Fact]
    public async Task Unknown_action_returns_not_found()
    {
        await using var sp = await Services(new FixedTime(Now));

        Assert.Equal(CorporateActionErrors.NotFound, (await Step(sp, Guid.NewGuid(), CorporateActionStep.Submit, Pia)).Error);
        Assert.Equal(InstrumentErrors.NotFound, (await Create(sp,
            new CreateCorporateActionCommand(Guid.NewGuid(), CorporateActionType.Delisting, null, null, null, Due, Pia))).Error);
    }

    [Fact]
    public async Task Duplicate_open_action_returns_duplicate_error()
    {
        await using var sp = await Services(new FixedTime(Now));
        var instrumentId = await NewInstrument(sp);
        var command = new CreateCorporateActionCommand(instrumentId, CorporateActionType.Split, 1, 4, null, Due, Pia);

        await Create(sp, command);

        Assert.Equal(CorporateActionErrors.Duplicate, (await Create(sp, command)).Error);
    }

    [Fact]
    public async Task Public_list_hides_drafts_and_submitted()
    {
        await using var sp = await Services(new FixedTime(Now));
        var instrumentId = await NewInstrument(sp);
        var draft = (await Create(sp, new CreateCorporateActionCommand(instrumentId, CorporateActionType.Delisting, null, null, null, Due, Pia))).Value;
        var approved = (await Create(sp, new CreateCorporateActionCommand(instrumentId, CorporateActionType.Split, 1, 4, null, Due, Pia))).Value;
        await Step(sp, approved, CorporateActionStep.Submit, Pia);
        await Step(sp, approved, CorporateActionStep.Approve, Fred);

        var visible = await sp.GetRequiredService<ListCorporateActionsQuery>().PublicForInstrument(instrumentId, default);

        Assert.Equal([approved], visible.Select(a => a.Id));
        Assert.Equal(2, (await sp.GetRequiredService<ListCorporateActionsQuery>().Worklist(default)).Count);
        Assert.NotEqual(draft, approved);
    }

    [Fact]
    public async Task Isin_change_to_taken_isin_is_rejected_at_creation()
    {
        await using var sp = await Services(new FixedTime(Now));
        var sap = await NewInstrument(sp);
        await using (var scope = sp.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<CreateInstrumentHandler>()
                .Handle(new CreateInstrumentCommand("DE0007236101", null, "Siemens AG", InstrumentType.Share, "EUR"), default);

        Assert.Equal(InstrumentErrors.DuplicateIsin, (await Create(sp, new CreateCorporateActionCommand(
            sap, CorporateActionType.IsinChange, null, null, "DE0007236101", Due, Pia))).Error);
    }

    [Fact]
    public async Task Unknown_type_is_rejected_instead_of_becoming_a_delisting()
    {
        await using var sp = await Services(new FixedTime(Now));

        var result = await Create(sp, new CreateCorporateActionCommand(
            await NewInstrument(sp), (CorporateActionType)99, null, null, null, Due, Pia));

        Assert.Equal(CorporateActionErrors.InvalidType, result.Error);
    }

    private static async Task<Result<Guid>> Create(IServiceProvider sp, CreateCorporateActionCommand command)
    {
        await using var scope = sp.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CreateCorporateActionHandler>().Handle(command, default);
    }

    private static async Task<Result> Step(IServiceProvider sp, Guid id, CorporateActionStep step, Actor user)
    {
        await using var scope = sp.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ChangeCorporateActionHandler>()
            .Handle(new ChangeCorporateActionCommand(id, step, user), default);
    }

    private async Task<ServiceProvider> Services(TimeProvider? time = null)
    {
        var sp = new ServiceCollection()
            .AddSingleton(time ?? TimeProvider.System)
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
        var action = CorporateAction.CreateSplit(instrument, SplitRatio.Create(1, 4).Value, effectiveAt, Pia).Value;
        scope.ServiceProvider.GetRequiredService<ICorporateActionRepository>().Add(action);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(default);
        return action.Id;
    }

    private static async Task Change(IServiceProvider sp, Guid id, Func<CorporateAction, Result> step)
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
