using Dapper;
using Npgsql;

namespace Portal.Instruments.Application.CorporateActions;

/// <remarks><see cref="EffectiveAt"/> ist UTC (<c>DateTimeKind.Utc</c>): Dapper liefert <c>timestamptz</c> als DateTime.</remarks>
public sealed record CorporateActionDto(
    Guid Id, Guid InstrumentId, string InstrumentName, string Isin, string Type, int? SplitOld, int? SplitNew, string? NewIsin,
    DateTime EffectiveAt, string Status, string CreatedBy, string? DecidedBy);

/// <summary>Lesende Seite (CQRS): Arbeitsliste für Pfleger/Freigeber und öffentliche Fakten je Instrument.</summary>
public sealed class ListCorporateActionsQuery(NpgsqlDataSource db)
{
    private const string Select = """
        SELECT a.id, a.instrument_id AS instrumentid, i.name AS instrumentname, i.isin, a.type, a.split_old AS splitold,
               a.split_new AS splitnew, a.new_isin AS newisin, a.effective_at AS effectiveat, a.status,
               a.created_by AS createdby, a.decided_by AS decidedby
        FROM corporate_actions a JOIN instruments i ON i.id = a.instrument_id
        """;

    public async Task<IReadOnlyList<CorporateActionDto>> Worklist(CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        return (await connection.QueryAsync<CorporateActionDto>(new CommandDefinition(
            $"{Select} ORDER BY a.id DESC LIMIT 200", cancellationToken: ct))).AsList(); // UUIDv7: neueste zuerst
    }

    /// <summary>Öffentlich: nur entschiedene Fakten – Entwürfe und Eingereichtes sind interne Arbeitsstände.</summary>
    public async Task<IReadOnlyList<CorporateActionDto>> PublicForInstrument(Guid instrumentId, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        return (await connection.QueryAsync<CorporateActionDto>(new CommandDefinition(
            $"{Select} WHERE a.instrument_id = @instrumentId AND a.status IN ('Approved','Effective','Cancelled') ORDER BY a.effective_at DESC",
            new { instrumentId }, cancellationToken: ct))).AsList();
    }
}
