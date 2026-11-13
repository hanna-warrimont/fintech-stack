using System.Diagnostics;
using System.Text.Json;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Portal.Instruments.Application;
using Portal.Instruments.Application.Abstractions;
using Portal.Instruments.Application.Instruments;
using Portal.Instruments.Domain.Abstractions;
using Portal.Instruments.Domain.Instruments;
using Portal.Instruments.Infrastructure;
using Portal.Messaging.Contracts;

namespace Portal.Tests.Instruments;

[Collection("infra")]
public sealed class InstrumentPersistenceTests(PostgresFixture postgres)
{
    private static readonly CreateInstrumentCommand Sap = new("DE0007164600", "716460", "SAP SE", InstrumentType.Share, "EUR");

    [Fact]
    public async Task Create_persists_and_writes_snapshot_to_outbox_in_same_transaction()
    {
        await using var sp = await Services();

        var id = (await Handle(sp, Sap)).Value;

        await using var db = Data(sp);
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT count(*) FROM instruments"));
        var row = Assert.Single(await Outbox(db));
        Assert.Equal("instruments.v1", row.Topic);
        Assert.Equal(id.ToString("D"), row.Key);
        Assert.Equal("InstrumentSnapshot", row.Type);
        Assert.Null(row.ProcessedAt);
        var envelope = JsonSerializer.Deserialize<Envelope<InstrumentSnapshot>>(row.Payload, JsonSerializerOptions.Web)!;
        Assert.Equal(1, envelope.Version);
        Assert.Equal("DE0007164600", envelope.Payload.Isin);
        Assert.Equal("Share", envelope.Payload.Type);
        Assert.Equal("Active", envelope.Payload.Status);
        Assert.Equal(1, envelope.Payload.Revision);
    }

    [Fact]
    public async Task Rename_writes_second_snapshot_with_higher_revision()
    {
        await using var sp = await Services();
        var id = (await Handle(sp, Sap)).Value;

        await using (var scope = sp.CreateAsyncScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<RenameInstrumentHandler>()
                .Handle(new RenameInstrumentCommand(id, "SAP"), CancellationToken.None)).IsSuccess);

        await using var db = Data(sp);
        var latest = (await Outbox(db)).Select(r => JsonSerializer.Deserialize<Envelope<InstrumentSnapshot>>(r.Payload, JsonSerializerOptions.Web)!)
            .MaxBy(e => e.Payload.Revision)!;
        Assert.Equal(2, (await Outbox(db)).Count);
        Assert.Equal(2, latest.Payload.Revision);
        Assert.Equal("SAP", latest.Payload.Name);
    }

    [Fact]
    public async Task Rename_unknown_returns_not_found()
    {
        await using var sp = await Services();
        await using var scope = sp.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<RenameInstrumentHandler>()
            .Handle(new RenameInstrumentCommand(Guid.NewGuid(), "X"), CancellationToken.None);

        Assert.Equal(InstrumentErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Duplicate_isin_returns_conflict()
    {
        await using var sp = await Services();
        await Handle(sp, Sap);

        var second = await Handle(sp, Sap with { Name = "Doppelt" });

        Assert.Equal(InstrumentErrors.DuplicateIsin, second.Error);
        await using var db = Data(sp);
        Assert.Single(await Outbox(db));
    }

    [Fact]
    public async Task Concurrent_duplicate_isin_returns_conflict()
    {
        await using var sp = await Services();

        var results = await Task.WhenAll(Handle(sp, Sap), Handle(sp, Sap));

        Assert.Single(results, r => r.IsSuccess);
        Assert.Single(results, r => r.Error == InstrumentErrors.DuplicateIsin);
        await using var db = Data(sp);
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT count(*) FROM instruments"));
        Assert.Single(await Outbox(db));
    }

    [Fact]
    public async Task Failed_save_writes_no_outbox_row()
    {
        await using var sp = await Services();
        await Handle(sp, Sap);

        await using (var scope = sp.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
            repository.Add(Instrument.Create(Isin.Create("DE0007164600").Value, null, "Doppelt", InstrumentType.Share, Currency.Create("EUR").Value).Value);
            await Assert.ThrowsAsync<DuplicateKeyException>(() =>
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None));
        }

        await using var db = Data(sp);
        Assert.Single(await Outbox(db));
    }

    [Fact]
    public async Task Outbox_captures_trace_parent()
    {
        await using var sp = await Services();
        using var source = new ActivitySource("test-request");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        string expected;
        using (var request = source.StartActivity("POST /instruments")!)
        {
            expected = request.Id!;
            await Handle(sp, Sap);
        }

        await using var db = Data(sp);
        Assert.Equal(expected, Assert.Single(await Outbox(db)).TraceParent);
    }

    [Fact]
    public async Task Get_returns_response_or_null()
    {
        await using var sp = await Services();
        var id = (await Handle(sp, Sap)).Value;
        var query = sp.GetRequiredService<GetInstrumentQuery>();

        var found = await query.Handle(id, CancellationToken.None);

        Assert.Equal("SAP SE", found!.Name);
        Assert.Equal("716460", found.Wkn);
        Assert.Equal("Share", found.Type);
        Assert.Null(await query.Handle(Guid.NewGuid(), CancellationToken.None));
    }

    private async Task<ServiceProvider> Services()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var sp = new ServiceCollection()
            .AddInstrumentApplication()
            .AddInstrumentInfrastructure(connectionString)
            .BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InstrumentDbContext>().Database.MigrateAsync();
        return sp;
    }

    private static async Task<Result<Guid>> Handle(IServiceProvider sp, CreateInstrumentCommand command)
    {
        await using var scope = sp.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CreateInstrumentHandler>().Handle(command, CancellationToken.None);
    }

    private static NpgsqlConnection Data(IServiceProvider sp) => sp.GetRequiredService<NpgsqlDataSource>().CreateConnection();

    private static async Task<List<OutboxRow>> Outbox(NpgsqlConnection db) =>
        (await db.QueryAsync<OutboxRow>(
            "SELECT topic, key, type, payload::text AS payload, trace_parent AS traceparent, processed_at AS processedat FROM outbox_messages"))
        .ToList();

    private sealed record OutboxRow(string Topic, string Key, string Type, string Payload, string? TraceParent, DateTime? ProcessedAt);
}
