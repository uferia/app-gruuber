using System.Text.Json;
using Gruuber.Orders.Domain;
using Gruuber.Orders.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gruuber.Orders.Application.Commands;

public class TransitionOrderHandler
{
    private readonly OrdersDbContext _db;
    private readonly ILogger<TransitionOrderHandler> _logger;

    public TransitionOrderHandler(OrdersDbContext db, ILogger<TransitionOrderHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ApplicationResult<TransitionOrderResponse>> HandleAsync(
        TransitionOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<OrderStatus>(command.NewStatus, ignoreCase: true, out var newStatus))
            return ApplicationResult<TransitionOrderResponse>.Failure("INVALID_STATUS", $"Unknown status '{command.NewStatus}'.", 400);

        var order = await _db.Orders.FindAsync(new object[] { command.OrderId }, cancellationToken);
        if (order is null)
            return ApplicationResult<TransitionOrderResponse>.Failure("ORDER_NOT_FOUND", "Order not found.", 404);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var transitioned = order.TryTransition(newStatus, command.ExpectedVersion);
        if (!transitioned)
            return ApplicationResult<TransitionOrderResponse>.Conflict(order.Id, order.Version);

        _db.Set<OrderOutboxEntry>().Add(new OrderOutboxEntry
        {
            EventType = $"order-events-{command.RegionId}",
            Payload = JsonSerializer.Serialize(new
            {
                EventName = "order_status_changed",
                OrderId = order.Id,
                NewStatus = newStatus.ToString(),
                OccurredAt = DateTime.UtcNow
            })
        });

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation("Order {OrderId} transitioned to {Status}", order.Id, newStatus);

        return ApplicationResult<TransitionOrderResponse>.Success(
            new TransitionOrderResponse(order.Id, order.Status.ToString()));
    }
}

public record TransitionOrderResponse(Guid OrderId, string Status);
