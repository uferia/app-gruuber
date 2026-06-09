namespace Gruuber.SharedKernel.Messaging;

/// <summary>
/// Abstract Factory — defines the contract for creating region-scoped outbox event messages.
/// Each module provides a concrete factory that builds its own outbox entry type.
/// </summary>
public interface IEventMessageFactory<TOutboxEntry>
{
    TOutboxEntry CreateRequested(int regionId, Guid entityId, object payload);
    TOutboxEntry CreateStatusChanged(int regionId, Guid entityId, string newStatus, Guid actorId);
    TOutboxEntry CreateFailed(int regionId, Guid entityId, string reason);
}
