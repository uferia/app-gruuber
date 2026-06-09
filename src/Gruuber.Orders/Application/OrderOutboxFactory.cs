using System.Text.Json;
using Gruuber.Orders.Infrastructure;
using Gruuber.SharedKernel.Messaging;

namespace Gruuber.Orders.Application;

/// <summary>
/// Abstract Factory (concrete) — builds order-domain outbox entries.
/// Implements IEventMessageFactory for generic lifecycle events,
/// plus order-specific overloads for richer payloads.
/// </summary>
public sealed class OrderOutboxFactory : IEventMessageFactory<OrderOutboxEntry>
{
    // --- IEventMessageFactory<OrderOutboxEntry> ---

    public OrderOutboxEntry CreateRequested(int regionId, Guid entityId, object payload) =>
        Build(regionId, payload);

    public OrderOutboxEntry CreateStatusChanged(int regionId, Guid entityId, string newStatus, Guid actorId) =>
        CreateOrderStatusChanged(regionId, entityId, newStatus, actorId);

    public OrderOutboxEntry CreateFailed(int regionId, Guid entityId, string reason) =>
        Build(regionId, new
        {
            EventName = "order_failed",
            OrderId = entityId,
            RegionId = regionId,
            Reason = reason,
            OccurredAt = DateTime.UtcNow
        });

    // --- Order-specific factory methods ---

    public OrderOutboxEntry CreateOrderCreated(int regionId, Guid orderId, Guid riderId,
        Guid restaurantId, Guid rideId, decimal surgeMultiplier, decimal? finalFare) =>
        Build(regionId, new
        {
            EventName = "order_created",
            OrderId = orderId,
            RiderId = riderId,
            RestaurantId = restaurantId,
            RideId = rideId,
            RegionId = regionId,
            SurgeMultiplier = surgeMultiplier,
            FinalFare = finalFare,
            OccurredAt = DateTime.UtcNow
        });

    public OrderOutboxEntry CreateOrderStatusChanged(int regionId, Guid orderId, string newStatus, Guid actorId) =>
        Build(regionId, new
        {
            EventName = "order_status_changed",
            OrderId = orderId,
            NewStatus = newStatus,
            ActorId = actorId,
            RegionId = regionId,
            OccurredAt = DateTime.UtcNow
        });

    public OrderOutboxEntry CreateOrderCancelled(int regionId, Guid orderId, string reason) =>
        Build(regionId, new
        {
            EventName = "order_cancelled",
            OrderId = orderId,
            RegionId = regionId,
            Reason = reason,
            RefundRequired = true,
            OccurredAt = DateTime.UtcNow
        });

    private static OrderOutboxEntry Build(int regionId, object payload) =>
        new()
        {
            EventType = $"order-events-{regionId}",
            Payload = JsonSerializer.Serialize(payload)
        };
}
