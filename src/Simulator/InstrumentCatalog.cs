using System.Collections.Concurrent;
using Portal.Messaging.Contracts;

namespace Portal.Simulator;

/// <summary>Die Instrumente, für die der Simulator Kurse erzeugt – gespeist aus <c>instruments.v1</c>.</summary>
public sealed class InstrumentCatalog
{
    private readonly ConcurrentDictionary<Guid, InstrumentSnapshot> _instruments = new();

    public IReadOnlyCollection<InstrumentSnapshot> Active =>
        _instruments.Values.Where(i => i.Status == "Active").ToList();

    /// <summary>Nur neuere Revisionen gewinnen – Snapshots können in falscher Reihenfolge ankommen.</summary>
    public void Apply(InstrumentSnapshot snapshot) =>
        _instruments.AddOrUpdate(snapshot.Id, snapshot, (_, current) => snapshot.Revision > current.Revision ? snapshot : current);
}
