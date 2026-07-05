using System.Text.Json;
using Gruuber.Orders.Domain;
using Gruuber.Orders.Infrastructure;
using Gruuber.SharedKernel.Catalog;
using Gruuber.SharedKernel.Payments;
using Gruuber.SharedKernel.Pricing;
using Gruuber.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Gruuber.Orders.Application.Commands;

public class CreateOrderHandler
{
    private readonly OrdersDbContext _db;
    private readonly IRestaurantCatalogReader _catalog;
    private readonly ISurgePricingService _surge;
    private readonly IOrderPaymentInitiator _payments;
    private readonly OrderPricingOptions _pricing;
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(
        OrdersDbContext db,
        IRestaurantCatalogReader catalog,
        ISurgePricingService surge,
        IOrderPaymentInitiator payments,
        OrderPricingOptions pricing,
        ILogger<CreateOrderHandler> logger)
    {
        _db = db;
        _catalog = catalog;
        _surge = surge;
        _payments = payments;
        _pricing = pricing;
        _logger = logger;
    }

    public async Task<ApplicationResult<CreateOrderResponse>> HandleAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var restaurant = await _catalog.GetRestaurantAsync(command.RestaurantId, cancellationToken);
        if (restaurant is null)
            return ApplicationResult<CreateOrderResponse>.Failure("RESTAURANT_NOT_FOUND", "Restaurant not found.", 404);

        if (!restaurant.IsApproved || !restaurant.IsOpen)
            return ApplicationResult<CreateOrderResponse>.Failure(
                "RESTAURANT_UNAVAILABLE", "Restaurant is not accepting orders right now.", 400);

        if (restaurant.RegionId != command.RegionId)
            return ApplicationResult<CreateOrderResponse>.Failure(
                "REGION_MISMATCH", "Restaurant is not in your region.", 400);

        var requestedIds = command.Items.Select(i => i.MenuItemId).Distinct().ToList();
        var menuItems = await _catalog.GetMenuItemsAsync(requestedIds, cancellationToken);
        var byId = menuItems.ToDictionary(m => m.Id);

        foreach (var requested in command.Items)
        {
            if (!byId.TryGetValue(requested.MenuItemId, out var menuItem) || menuItem.RestaurantId != command.RestaurantId)
                return ApplicationResult<CreateOrderResponse>.Failure(
                    "INVALID_MENU_ITEM", $"Menu item {requested.MenuItemId} does not belong to this restaurant.", 400);
            if (!menuItem.IsAvailable)
                return ApplicationResult<CreateOrderResponse>.Failure(
                    "ITEM_UNAVAILABLE", $"'{menuItem.Name}' is currently unavailable.", 400);
        }

        var order = Order.CreateForDelivery(
            command.RiderId, command.RestaurantId, command.RegionId,
            command.DeliveryLat, command.DeliveryLng, command.PaymentMethod);

        foreach (var requested in command.Items)
            order.AddItem(requested.MenuItemId, requested.Quantity, byId[requested.MenuItemId].Price);

        order.SetDeliveryFee(_pricing.DeliveryFee);
        var surgeResult = await _surge.ResolveAsync(
            command.RegionId, "food", order.TotalAmount + order.DeliveryFee, cancellationToken);
        order.ApplySurge(surgeResult.BaseFare, surgeResult.Multiplier, surgeResult.Reason);

        var total = order.FinalFare ?? order.TotalAmount + order.DeliveryFee;
        var currency = byId[command.Items[0].MenuItemId].Currency;

        var outbox = new OrderOutboxEntry
        {
            EventType = $"order-events-{command.RegionId}",
            Payload = JsonSerializer.Serialize(new
            {
                EventName = "order_placed",
                OrderId = order.Id,
                order.RiderId,
                order.RestaurantId,
                RegionId = command.RegionId,
                Total = total,
                PaymentMethod = order.PaymentMethod.ToString(),
                OccurredAt = DateTime.UtcNow
            })
        };

        await using (var tx = await _db.Database.BeginTransactionAsync(cancellationToken))
        {
            _db.Orders.Add(order);
            _db.Set<OrderOutboxEntry>().Add(outbox);
            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }

        OrderPaymentResult payment;
        try
        {
            payment = await _payments.InitiateForOrderAsync(
                new OrderPaymentRequest(order.Id, command.RiderId, total, currency, command.PaymentMethod, command.RegionId),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Payment initiation failed for order {OrderId}; cancelling order", order.Id);
            order.TryCancel(OrderCancellationReason.PaymentFailed, null, "system", order.Version);
            _db.Set<OrderOutboxEntry>().Add(new OrderOutboxEntry
            {
                EventType = $"order-events-{command.RegionId}",
                Payload = JsonSerializer.Serialize(new
                {
                    EventName = "order_cancelled",
                    OrderId = order.Id,
                    order.RestaurantId,
                    RegionId = command.RegionId,
                    Reason = OrderCancellationReason.PaymentFailed.ToString(),
                    OccurredAt = DateTime.UtcNow
                })
            });
            await _db.SaveChangesAsync(cancellationToken);
            return ApplicationResult<CreateOrderResponse>.Failure(
                "PAYMENT_FAILED", "Payment could not be initiated; the order was cancelled.", 400);
        }

        _logger.LogInformation(
            "Order {OrderId} placed for rider {RiderId} in region {RegionId} total {Total} {Currency} payment {PaymentId}",
            order.Id, order.RiderId, command.RegionId, total, currency, payment.PaymentId);

        FareEstimate? fareResponse = null;
        if (order.BaseFare.HasValue)
        {
            fareResponse = new FareEstimate(
                order.BaseFare.Value,
                order.FinalFare!.Value,
                order.SurgeMultiplier > 1.0m ? order.SurgeMultiplier : null,
                order.SurgeReason);
        }

        return ApplicationResult<CreateOrderResponse>.Accepted(
            new CreateOrderResponse(order.Id, order.Status.ToString(), payment.PaymentId, total, fareResponse));
    }
}
