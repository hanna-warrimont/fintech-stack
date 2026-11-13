namespace Portal.Instruments.Application.Abstractions;

/// <summary>Speichert alle Änderungen einer Anfrage atomar – inklusive der Outbox-Nachrichten.</summary>
public interface IUnitOfWork
{
    /// <exception cref="DuplicateKeyException">Eine Eindeutigkeitsregel der Datenbank wurde verletzt.</exception>
    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>Datenbank-neutrale Form einer Unique-Verletzung – Application soll nichts über Postgres wissen.</summary>
public sealed class DuplicateKeyException(Exception inner) : Exception("Eindeutigkeitsregel verletzt.", inner);
