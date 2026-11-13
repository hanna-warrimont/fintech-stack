using System.Text.RegularExpressions;
using Dapper;
using Npgsql;

namespace Portal.Search;

public sealed record SearchHit(Guid Id, string Isin, string? Wkn, string Name, string Type, string Currency, string Status);

/// <summary>Volltextsuche über Name, ISIN und WKN – Präfix-Treffer, alle Wörter müssen passen.</summary>
public sealed partial class SearchQuery(NpgsqlDataSource db)
{
    private const int Limit = 20;

    public async Task<IReadOnlyList<SearchHit>> Handle(string? q, CancellationToken ct)
    {
        // Nur Buchstaben/Ziffern bleiben übrig – damit kommt keine tsquery-Syntax (& | ! : * ') vom Nutzer durch.
        var tokens = NonWord().Split(q ?? string.Empty)
            .Where(t => t.Length > 0)
            .Take(5)
            .Select(t => t.ToLowerInvariant() + ":*")
            .ToList();

        await using var connection = await db.OpenConnectionAsync(ct);
        var hits = tokens.Count == 0
            ? await connection.QueryAsync<SearchHit>(new CommandDefinition(
                "SELECT id, isin, wkn, name, type, currency, status FROM instruments ORDER BY lower(name), name LIMIT @Limit",
                new { Limit }, cancellationToken: ct))
            : await connection.QueryAsync<SearchHit>(new CommandDefinition("""
                SELECT id, isin, wkn, name, type, currency, status FROM instruments
                WHERE search @@ to_tsquery('simple', @query)
                ORDER BY ts_rank(search, to_tsquery('simple', @query)) DESC, lower(name)
                LIMIT @Limit
                """, new { query = string.Join(" & ", tokens), Limit }, cancellationToken: ct));
        return hits.ToList();
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();
}
