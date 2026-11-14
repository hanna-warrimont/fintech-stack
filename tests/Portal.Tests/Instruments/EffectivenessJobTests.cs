using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Portal.Instruments.Application;
using Portal.Instruments.Application.CorporateActions;
using Portal.Instruments.Application.Instruments;
using Portal.Instruments.Domain.CorporateActions;
using Portal.Instruments.Domain.Instruments;
using Portal.Instruments.Infrastructure;
using Portal.Instruments.Infrastructure.CorporateActions;
using Portal.Messaging.Contracts;

namespace Portal.Tests.Instruments;

[Collection("infra")]
public sealed class EffectivenessJobTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Due_action_becomes_effective_with_outbox_in_same_transaction()
    {
        var (sp, time) = await Services();
        await using var _ = sp;
        var id = await Approved(sp, await NewInstrument(sp), CorporateActionType.Split, T0.AddMinutes(2));
        time.Now = T0.AddMinutes(3);

        Assert.Equal(1, await Job(sp, time).RunOnceAsync(default));

        await using var db = Data(sp);
        Assert.Equal("Effective", await db.ExecuteScalarAsync<string>("SELECT status FROM corporate_actions WHERE id = @id", new { id }));
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT count(*) FROM outbox_messages WHERE type = @t", new { t = CorporateActionEvent.BecameEffective }));
    }

    [Fact]
    public async Task Not_yet_due_action_stays_approved()
    {
        var (sp, time) = await Services();
        await using var _ = sp;
        await Approved(sp, await NewInstrument(sp), CorporateActionType.Split, T0.AddMinutes(2));
        time.Now = T0.AddMinutes(1);

        Assert.Equal(0, await Job(sp, time).RunOnceAsync(default));
    }

    [Fact]
    public async Task Isin_change_updates_instrument_and_writes_snapshot()
    {
        var (sp, time) = await Services();
        await using var _ = sp;
        var instrumentId = await NewInstrument(sp);
        await Approved(sp, instrumentId, CorporateActionType.IsinChange, T0.AddMinutes(2), newIsin: "US0378331005");
        time.Now = T0.AddMinutes(3);

        await Job(sp, time).RunOnceAsync(default);

        await using var db = Data(sp);
        Assert.Equal("US0378331005", await db.ExecuteScalarAsync<string>("SELECT isin FROM instruments WHERE id = @instrumentId", new { instrumentId }));
        var snapshot = (await db.QueryAsync<string>("SELECT payload::text FROM outbox_messages WHERE type = @t ORDER BY occurred_at", new { t = InstrumentSnapshot.MessageType }))
            .Select(InstrumentSnapshot.TryParse).Last()!.Payload;
        Assert.Equal(("US0378331005", 2), (snapshot.Isin, snapshot.Revision));
    }

    [Fact]
    public async Task Delisting_sets_instrument_delisted()
    {
        var (sp, time) = await Services();
        await using var _ = sp;
        var instrumentId = await NewInstrument(sp);
        await Approved(sp, instrumentId, CorporateActionType.Delisting, T0.AddMinutes(2));
        time.Now = T0.AddMinutes(3);

        await Job(sp, time).RunOnceAsync(default);

        await using var db = Data(sp);
        Assert.Equal("Delisted", await db.ExecuteScalarAsync<string>("SELECT status FROM instruments WHERE id = @instrumentId", new { instrumentId }));
    }

    [Fact]
    public async Task Two_jobs_in_parallel_make_each_action_effective_once()
    {
        var (sp, time) = await Services();
        await using var _ = sp;
        var instrumentId = await NewInstrument(sp);
        // 20 Splits an 20 Tagen (eine offene Maßnahme pro Art und Tag), alle bei T0 freigegeben
        for (var day = 1; day <= 20; day++)
            await Approved(sp, instrumentId, CorporateActionType.Split, T0.AddDays(day));
        time.Now = T0.AddDays(30);
        var jobs = new[] { Job(sp, time), Job(sp, time) };

        var counts = await Task.WhenAll(jobs.Select(async job =>
        {
            var total = 0;
            for (var i = 0; i < 3; i++) total += await job.RunOnceAsync(default);
            return total;
        }));

        Assert.Equal(20, counts.Sum());
        await using var db = Data(sp);
        Assert.Equal(20, await db.ExecuteScalarAsync<int>("SELECT count(*) FROM outbox_messages WHERE type = @t", new { t = CorporateActionEvent.BecameEffective }));
        Assert.Equal(20, await db.ExecuteScalarAsync<int>("SELECT count(*) FROM corporate_actions WHERE status = 'Effective'"));
    }

    [Fact]
    public async Task Inapplicable_action_is_cancelled_and_others_still_become_effective()
    {
        var (sp, time) = await Services();
        await using var _ = sp;
        var instrumentId = await NewInstrument(sp);
        var first = await Approved(sp, instrumentId, CorporateActionType.Delisting, T0.AddMinutes(2));
        var second = await Approved(sp, instrumentId, CorporateActionType.Delisting, T0.AddDays(1)); // nach dem ersten nicht mehr anwendbar
        var split = await Approved(sp, instrumentId, CorporateActionType.Split, T0.AddDays(2));
        time.Now = T0.AddDays(3);

        await Job(sp, time).RunOnceAsync(default);

        Assert.Equal(["Effective", "Cancelled", "Effective"], await Statuses(sp, first, second, split));
        Assert.Equal(0, await Job(sp, time).RunOnceAsync(default)); // nichts bleibt hängen
    }

    [Fact]
    public async Task Isin_change_to_meanwhile_taken_isin_is_cancelled_and_others_still_become_effective()
    {
        var (sp, time) = await Services();
        await using var _ = sp;
        var sap = await NewInstrument(sp);
        var change = await Approved(sp, sap, CorporateActionType.IsinChange, T0.AddMinutes(2), newIsin: "US0378331005");
        var split = await Approved(sp, sap, CorporateActionType.Split, T0.AddMinutes(3));
        await NewInstrument(sp, "US0378331005"); // ISIN nach der Freigabe vergeben
        time.Now = T0.AddMinutes(5);

        await Job(sp, time).RunOnceAsync(default);

        Assert.Equal(["Cancelled", "Effective"], await Statuses(sp, change, split));
        await using var db = Data(sp);
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT count(*) FROM outbox_messages WHERE type = @t", new { t = CorporateActionEvent.Cancelled }));
    }

    private static async Task<string[]> Statuses(IServiceProvider sp, params Guid[] ids)
    {
        await using var db = Data(sp);
        var statuses = (await db.QueryAsync<(Guid Id, string Status)>("SELECT id, status FROM corporate_actions")).ToDictionary(r => r.Id, r => r.Status);
        return ids.Select(id => statuses[id]).ToArray();
    }

    private async Task<(ServiceProvider Sp, FixedTime Time)> Services()
    {
        var time = new FixedTime(T0);
        var sp = new ServiceCollection()
            .AddSingleton<TimeProvider>(time)
            .AddLogging()
            .AddInstrumentApplication()
            .AddInstrumentInfrastructure(await postgres.CreateDatabaseAsync())
            .BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InstrumentDbContext>().Database.MigrateAsync();
        return (sp, time);
    }

    private static EffectivenessJob Job(IServiceProvider sp, TimeProvider time) =>
        new(sp.GetRequiredService<IServiceScopeFactory>(), time, NullLogger<EffectivenessJob>.Instance);

    private static async Task<Guid> NewInstrument(IServiceProvider sp, string isin = "DE0007164600")
    {
        await using var scope = sp.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<CreateInstrumentHandler>()
            .Handle(new CreateInstrumentCommand(isin, null, "Instrument " + isin, InstrumentType.Share, "EUR"), default)).Value;
    }

    private static async Task<Guid> Approved(IServiceProvider sp, Guid instrumentId, CorporateActionType type, DateTimeOffset effectiveAt, string? newIsin = null)
    {
        Guid id;
        await using (var scope = sp.CreateAsyncScope())
            id = (await scope.ServiceProvider.GetRequiredService<CreateCorporateActionHandler>().Handle(
                new CreateCorporateActionCommand(instrumentId, type, 1, 4, newIsin, effectiveAt, "pia"), default)).Value;
        foreach (var (step, user) in new[] { (CorporateActionStep.Submit, "pia"), (CorporateActionStep.Approve, "fred") })
        {
            await using var scope = sp.CreateAsyncScope();
            Assert.True((await scope.ServiceProvider.GetRequiredService<ChangeCorporateActionHandler>()
                .Handle(new ChangeCorporateActionCommand(id, step, user), default)).IsSuccess);
        }
        return id;
    }

    private static NpgsqlConnection Data(IServiceProvider sp) => sp.GetRequiredService<NpgsqlDataSource>().CreateConnection();
}
