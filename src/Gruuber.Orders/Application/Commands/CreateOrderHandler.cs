using System.Text.Json;
using Gruuber.Orders.Domain;
using Gruuber.Orders.Infrastructure;
using Gruuber.SharedKernel.Pricing;
using Gruuber.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Gruuber.Orders.Application.Commands;

public class CreateOrderHandler
{
    private readonly OrdersDbContext _db;
    private readonly ISurgePricingService _surge;
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(OrdersDbContext db, ISurgePricingService surge, ILogger<CreateOrderHandler> logger)
    {
        _db = db;
        _surge = surge;
        _logger = logger;
    }

    public async Task<ApplicationResult<CreateOrderResponse>> HandleAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var order = Order.Create(command.RiderId, command.RestaurantId, command.RideId, command.RegionId);
        foreach (var item in command.Items)
            order.AddItem(item.MenuItemId, item.Quantity, item.Price);

        var surgeResult = await _surge.ResolveAsync(command.RegionId, "food", order.TotalAmount, cancellationToken);
        order.ApplySurge(surgeResult.BaseFare, surgeResult.Multiplier, surgeResult.Reason);

        var outbox = new OrderOutboxEntry
        {
            EventType = $"order-events-{command.RegionId}",
            Payload = JsonSerializer.Serialize(new
            {
                EventName = "order_created",
                OrderId = order.Id,
                order.RiderId,
                order.RestaurantId,
                order.RideId,
                RegionId = command.RegionId,
                SurgeMultiplier = order.SurgeMultiplier,
                FinalFare = order.FinalFare,
                OccurredAt = DateTime.UtcNow
            })
        };

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.Orders.Add(order);
        _db.Set<OrderOutboxEntry>().Add(outbox);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Order {OrderId} created for rider {RiderId} in region {RegionId} surge={SurgeMul}x",
            order.Id, order.RiderId, command.RegionId, order.SurgeMultiplier);

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
            new CreateOrderResponse(order.Id, order.Status.ToString(), fareResponse));
    }
}
