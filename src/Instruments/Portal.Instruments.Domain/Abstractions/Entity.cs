namespace Portal.Instruments.Domain.Abstractions;

public interface IDomainEvent;

/// <summary>Basisklasse für Aggregate: sammelt Domain Events, die beim Speichern ausgewertet werden.</summary>
public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
