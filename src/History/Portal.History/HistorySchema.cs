using Dapper;
using Npgsql;

namespace Portal.History;

/// <summary>
/// Schema der History-Datenbank. Zwei Arten von Daten, mit verschiedener Haltbarkeit:
/// <list type="bullet">
/// <item><c>candles_1m</c>, <c>instrument_seq</c>: abgeleitet, jederzeit aus <c>ticks.v1</c> neu aufbaubar (soweit dessen
/// Retention reicht). Darum <c>CREATE TABLE IF NOT EXISTS</c> statt Migrationen – im Zweifel löschen und neu aufbauen.</item>
/// <item><c>adjustments</c> (Split-Faktoren): <b>dauerhafter Zustand</b>. Aus <c>corporate-actions.v1</c> kommen sie nur, solange
/// dessen Retention reicht – lokal unbegrenzt, in Azure (Event Hubs Standard) höchstens 7 Tage. Der Neuaufbau der Kerzen lässt
/// sie deshalb stehen. Eine Schemaänderung an dieser Tabelle braucht eine echte Migration, kein Löschen und Neuanlegen.</item>
/// </list>
/// </summary>
public static class HistorySchema
{
    public static async Task EnsureAsync(NpgsqlDataSource db, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            CREATE TABLE IF NOT EXISTS candles_1m (
              instrument_id uuid NOT NULL, bucket timestamptz NOT NULL,
              open numeric(18,4) NOT NULL, high numeric(18,4) NOT NULL, low numeric(18,4) NOT NULL, close numeric(18,4) NOT NULL,
              volume bigint NOT NULL, open_time timestamptz NOT NULL, close_time timestamptz NOT NULL,
              PRIMARY KEY (instrument_id, bucket));
            CREATE TABLE IF NOT EXISTS instrument_seq (instrument_id uuid PRIMARY KEY, last_seq bigint NOT NULL);
            CREATE TABLE IF NOT EXISTS adjustments (corporate_action_id uuid PRIMARY KEY, instrument_id uuid NOT NULL,
              valid_until timestamptz NOT NULL, factor numeric(18,8) NOT NULL);
            CREATE TABLE IF NOT EXISTS processed_messages (message_id uuid PRIMARY KEY, processed_at timestamptz NOT NULL);
            """, cancellationToken: ct));
    }
}
