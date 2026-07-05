using System.Text.Json;
using Gruuber.Orders.Domain;
using Gruuber.Orders.Infrastructure;
using Gruuber.SharedKernel.Catalog;
using Gruuber.SharedKernel.Payments;
using Gruuber.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Gruuber.Orders.Application.Commands;

public class TransitionOrderHandler
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> LegalTransitions = new()
    {
        [OrderStatus.Placed] = new[] { OrderStatus.Accepted, OrderStatus.Cancelled },
        [OrderStatus.Accepted] = new[] { OrderStatus.Preparing },
        [OrderStatus.Preparing] = new[] { OrderStatus.Ready },
        [OrderStatus.Ready] = new[] { OrderStatus.PickedUp, OrderStatus.Cancelled },
        [OrderStatus.PickedUp] = new[] { OrderStatus.Delivered }
    };

    private readonly OrdersDbContext _db;
    private readonly IRestaurantCatalogReader _catalog;
    private readonly ILogger<TransitionOrderHandler> _logger;

    public TransitionOrderHandler(
        OrdersDbContext db,
        IRestaurantCatalogReader catalog,
        ILogger<TransitionOrderHandler> logger)
    {
        _db = db;
        _catalog = catalog;
        _logger = logger;
    }

    public async Task<ApplicationResult<TransitionOrderResponse>> HandleAsync(
        TransitionOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<OrderStatus>(command.NewStatus, ignoreCase: true, out var newStatus))
            return ApplicationResult<TransitionOrderResponse>.Failure(
                "INVALID_STATUS", $"Unknown status '{command.NewStatus}'.", 400);

        var order = await _db.Orders.FindAsync(new object[] { command.OrderId }, cancellationToken);
        if (order is null)
            return ApplicationResult<TransitionOrderResponse>.Failure("ORDER_NOT_FOUND", "Order not found.", 404);

        if (!LegalTransitions.TryGetValue(order.Status, out var allowed) || !allowed.Contains(newStatus))
            return ApplicationResult<TransitionOrderResponse>.Failure(
                "INVALID_TRANSITION", $"Cannot transition from {order.Status} to {newStatus}.", 400);

        var denied = await AuthorizeAsync(order, newStatus, command, cancellationToken);
        if (denied is not null)
            return denied;

        OrderCancellationReason? reason = null;
        if (newStatus == OrderStatus.Cancelled)
        {
            if (string.IsNullOrWhiteSpace(command.Reason))
                return ApplicationResult<TransitionOrderResponse>.Failure(
                    "REASON_REQUIRED", "A cancellation reason is required.", 400);
            if (!Enum.TryParse<OrderCancellationReason>(command.Reason, ignoreCase: true, out var parsed))
                return ApplicationResult<TransitionOrderResponse>.Failure(
                    "INVALID_REASON", $"Unknown cancellation reason '{command.Reason}'.", 400);
            if (!CancellationPolicy.IsAllowed(parsed, command.ActorRole))
                return ApplicationResult<TransitionOrderResponse>.Failure(
                    "REASON_NOT_ALLOWED", $"Reason '{parsed}' is not valid for role '{command.ActorRole}'.", 400);
            reason = parsed;
        }

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var transitioned = newStatus == OrderStatus.Cancelled
            ? order.TryCancel(reason!.Value, command.Note, command.ActorRole, command.ExpectedVersion)
            : order.TryTransition(newStatus, command.ExpectedVersion);
        if (!transitioned)
            return ApplicationResult<TransitionOrderResponse>.Conflict(order.Id, order.Version);

        var revenue = order.FinalFare ?? order.TotalAmount;

        _db.Set<OrderOutboxEntry>().Add(new OrderOutboxEntry
        {
            EventType = $"order-events-{command.RegionId}",
            Payload = JsonSerializer.Serialize(new
            {
                EventName = $"order_{newStatus.ToString().ToLowerInvariant()}",
                OrderId = order.Id,
                order.RestaurantId,
                order.RiderId,
                RegionId = command.RegionId,
                Revenue = revenue,
                Reason = order.CancellationReason?.ToString(),
                OccurredAt = DateTime.UtcNow
            })
        });

        if (newStatus == OrderStatus.Cancelled && order.PaymentMethod == PaymentMethod.CardMock)
        {
            _db.Set<OrderOutboxEntry>().Add(new OrderOutboxEntry
            {
                EventType = $"order-events-{command.RegionId}",
                Payload = JsonSerializer.Serialize(new
                {
                    EventName = "payment_refund_requested",
                    OrderId = order.Id,
                    Amount = revenue,
                    Reason = order.CancellationReason?.ToString(),
                    RegionId = command.RegionId,
                    OccurredAt = DateTime.UtcNow
                })
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Order {OrderId} transitioned to {Status} by {ActorRole} {ActorUserId}",
            order.Id, newStatus, command.ActorRole, command.ActorUserId);

        return ApplicationResult<TransitionOrderResponse>.Success(
            new TransitionOrderResponse(order.Id, order.Status.ToString()));
    }

    private async Task<ApplicationResult<TransitionOrderResponse>?> AuthorizeAsync(
        Order order,
        OrderStatus newStatus,
        TransitionOrderCommand command,
        CancellationToken cancellationToken)
    {
        switch (newStatus)
        {
            case OrderStatus.Accepted:
            case OrderStatus.Preparing:
            case OrderStatus.Ready:
                return await IsRestaurantOwnerAsync(order, command, cancellationToken) ? null : Forbidden();

            case OrderStatus.PickedUp:
            case OrderStatus.Delivered:
                return command.ActorRole == "driver" && order.DriverId == command.ActorUserId ? null : Forbidden();

            case OrderStatus.Cancelled when order.Status == OrderStatus.Ready:
                return command.ActorRole == "system" ? null : Forbidden();

            case OrderStatus.Cancelled: // from Placed
                if (command.ActorRole is "system" or "admin")
                    return null;
                if (command.ActorRole == "rider")
                    return order.RiderId == command.ActorUserId ? null : Forbidden();
                if (command.ActorRole == "restaurant")
                    return await IsRestaurantOwnerAsync(order, command, cancellationToken) ? null : Forbidden();
                return Forbidden();

            default:
                return Forbidden();
        }
    }

    private async Task<bool> IsRestaurantOwnerAsync(Order order, TransitionOrderCommand command, CancellationToken cancellationToken)
    {
        if (command.ActorRole != "restaurant")
            return false;
        var restaurant = await _catalog.GetRestaurantAsync(order.RestaurantId, cancellationToken);
        return restaurant is not null && restaurant.OwnerUserId == command.ActorUserId;
    }

    private static ApplicationResult<TransitionOrderResponse> Forbidden() =>
        ApplicationResult<TransitionOrderResponse>.Failure(
            "FORBIDDEN", "You are not allowed to perform this transition.", 403);
}

public record TransitionOrderResponse(Guid OrderId, string Status);
