using Dapper;
using Npgsql;
using Portal.Messaging.Contracts;

namespace Portal.Search;

/// <summary>
/// Schreibt Snapshots in die Lese-Tabelle. Zwei Schutzmechanismen, keiner ersetzt den anderen:
/// Inbox (<c>processed_messages</c>) gegen Duplikate derselben Nachricht, <c>revision</c> gegen falsche Reihenfolge.
/// </summary>
public sealed class InstrumentProjection(NpgsqlDataSource db)
{
    /// <returns><c>false</c>, wenn die Nachricht schon verarbeitet war.</returns>
    public async Task<bool> ApplyAsync(Envelope<InstrumentSnapshot> envelope, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        var isNew = await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO processed_messages (message_id, processed_at) VALUES (@MessageId, now()) ON CONFLICT DO NOTHING",
            new { envelope.MessageId }, transaction, cancellationToken: ct)) == 1;
        if (!isNew) return false; // Rollback beim Dispose

        var s = envelope.Payload;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO instruments (id, isin, wkn, name, type, currency, status, revision)
            VALUES (@Id, @Isin, @Wkn, @Name, @Type, @Currency, @Status, @Revision)
            ON CONFLICT (id) DO UPDATE SET
              isin = excluded.isin, wkn = excluded.wkn, name = excluded.name, type = excluded.type,
              currency = excluded.currency, status = excluded.status, revision = excluded.revision
            WHERE instruments.revision < excluded.revision
            """, new { s.Id, s.Isin, s.Wkn, s.Name, s.Type, s.Currency, s.Status, s.Revision }, transaction, cancellationToken: ct));

        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<bool> IsEmptyAsync(CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        return !await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS (SELECT 1 FROM instruments)", cancellationToken: ct));
    }

    /// <summary>Leert Projektion UND Inbox – sonst würde der Neuaufbau alle Nachrichten als „schon verarbeitet“ verwerfen.</summary>
    public async Task ClearAsync(CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("TRUNCATE instruments, processed_messages", cancellationToken: ct));
    }
}
