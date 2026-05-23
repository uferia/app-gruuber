using Gruuber.Orders.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Orders.Application.Queries;

public class GetOrderItemsHandler
{
    private readonly OrdersDbContext _db;

    public GetOrderItemsHandler(OrdersDbContext db) => _db = db;

    public async Task<ApplicationResult<List<OrderItemDto>>> HandleAsync(
        GetOrderItemsQuery query,
        CancellationToken cancellationToken = default)
    {
        var orderExists = await _db.Orders.AnyAsync(o => o.Id == query.OrderId, cancellationToken);
        if (!orderExists)
            return ApplicationResult<List<OrderItemDto>>.Failure("ORDER_NOT_FOUND", "Order not found.", 404);

        var items = await _db.Orders
            .Where(o => o.Id == query.OrderId)
            .SelectMany(o => o.Items)
            .Select(i => new OrderItemDto(i.MenuItemId, i.Quantity, i.Price))
            .ToListAsync(cancellationToken);

        return ApplicationResult<List<OrderItemDto>>.Success(items);
    }
}
