using Dapper;
using Npgsql;
using Portal.Messaging.Contracts;

namespace Portal.History;

/// <summary>
/// Verteilte Folge eines Splits: Aus <c>CorporateActionBecameEffective</c> wird ein Anpassungsfaktor
/// <c>(instrumentId, gültigBis, faktor)</c>. Idempotenter Consumer (Inbox): Die <c>messageId</c> wird in DERSELBEN
/// Transaktion gespeichert wie der Faktor – eine doppelt zugestellte Nachricht ändert nichts.
/// (Anders als Ticks: Die sind zu viele für eine Inbox und werden über <c>seq</c> dedupliziert.)
/// </summary>
public sealed class AdjustmentStore(NpgsqlDataSource db)
{
    /// <returns><c>false</c>, wenn die Nachricht schon verarbeitet war.</returns>
    public async Task<bool> ApplyAsync(Envelope<CorporateActionEvent> message, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var fresh = await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO processed_messages (message_id, processed_at) VALUES (@MessageId, now()) ON CONFLICT DO NOTHING",
            new { message.MessageId }, tx, cancellationToken: ct));
        if (fresh == 0) return false;

        if (message is { Type: CorporateActionEvent.BecameEffective, Payload: { Kind: "Split", SplitOld: { } old, SplitNew: { } @new } e })
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO adjustments (corporate_action_id, instrument_id, valid_until, factor)
                VALUES (@CorporateActionId, @InstrumentId, @EffectiveAt, @Factor) ON CONFLICT DO NOTHING
                """, new { e.CorporateActionId, e.InstrumentId, e.EffectiveAt, Factor = (decimal)old / @new }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return true;
    }
}
