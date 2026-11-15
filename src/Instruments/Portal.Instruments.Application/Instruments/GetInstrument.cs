using System.Data.Common;
using Dapper;

namespace Portal.Instruments.Application.Instruments;

public sealed record InstrumentResponse(Guid Id, string Isin, string? Wkn, string Name, string Type, string Currency, string Status);

/// <summary>Lesende Seite (CQRS): direkt per SQL, am Aggregat und EF vorbei.</summary>
public sealed class GetInstrumentQuery(DbDataSource db)
{
    public async Task<InstrumentResponse?> Handle(Guid id, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<InstrumentResponse>(new CommandDefinition(
            "SELECT id, isin, wkn, name, type, currency, status FROM instruments WHERE id = @id",
            new { id }, cancellationToken: ct));
    }
}
