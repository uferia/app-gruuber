using Gruuber.Orders.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Orders.Application.Queries;

public class GetOrderHandler
{
    private readonly OrdersDbContext _db;

    public GetOrderHandler(OrdersDbContext db) => _db = db;

    public async Task<ApplicationResult<OrderResponse>> HandleAsync(
        GetOrderQuery query,
        CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == query.OrderId, cancellationToken);

        if (order is null)
            return ApplicationResult<OrderResponse>.Failure("ORDER_NOT_FOUND", "Order not found.", 404);

        var dto = new OrderResponse(
            order.Id,
            order.RestaurantId,
            order.Status.ToString(),
            order.Items.Select(i => new OrderItemDto(i.MenuItemId, i.Quantity, i.Price)).ToList());

        return ApplicationResult<OrderResponse>.Success(dto);
    }
}
