using Dapper;
using Npgsql;

namespace Portal.Search;

/// <summary>
/// Schema der Lese-Datenbank. Keine Migrationen: Die Projektion ist jederzeit aus dem Topic neu aufbaubar.
/// </summary>
public static class SearchSchema
{
    public static async Task EnsureAsync(NpgsqlDataSource db, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            CREATE TABLE IF NOT EXISTS instruments (
              id uuid PRIMARY KEY, isin text NOT NULL, wkn text, name text NOT NULL, type text NOT NULL,
              currency text NOT NULL, status text NOT NULL, revision int NOT NULL,
              search tsvector GENERATED ALWAYS AS (to_tsvector('simple', name || ' ' || isin || ' ' || coalesce(wkn, ''))) STORED);
            CREATE INDEX IF NOT EXISTS ix_instruments_search ON instruments USING gin (search);
            CREATE TABLE IF NOT EXISTS processed_messages (message_id uuid PRIMARY KEY, processed_at timestamptz NOT NULL);
            """, cancellationToken: ct));
    }
}
