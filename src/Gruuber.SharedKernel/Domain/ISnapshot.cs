namespace Gruuber.SharedKernel.Domain;

/// <summary>
/// Memento pattern — a snapshot of an entity's state at a point in time.
/// The originator creates snapshots; the caretaker stores and retrieves them.
/// </summary>
public interface ISnapshot<TId>
{
    TId EntityId { get; }
    long Version { get; }
    DateTime CapturedAt { get; }
}

/// <summary>Originator — the entity that can produce and restore from a snapshot.</summary>
public interface ISnapshotOriginator<TSnapshot>
{
    TSnapshot CaptureSnapshot();
    void RestoreFromSnapshot(TSnapshot snapshot);
}

/// <summary>Caretaker — persists and retrieves snapshots (implemented per-module).</summary>
public interface ISnapshotRepository<TSnapshot, TId>
    where TSnapshot : ISnapshot<TId>
{
    Task SaveAsync(TSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<TSnapshot?> GetLatestAsync(TId entityId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TSnapshot>> GetHistoryAsync(TId entityId, CancellationToken cancellationToken = default);
}
