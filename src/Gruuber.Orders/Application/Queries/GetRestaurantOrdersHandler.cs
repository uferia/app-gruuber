using Gruuber.Orders.Domain;
using Gruuber.Orders.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Orders.Application.Queries;

public class GetRestaurantOrdersHandler
{
    private readonly OrdersDbContext _db;

    public GetRestaurantOrdersHandler(OrdersDbContext db)
    {
        _db = db;
    }

    public async Task<ApplicationResult<PagedOrders>> HandleAsync(
        GetRestaurantOrdersQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 50);

        var dbQuery = _db.Orders.AsNoTracking().Where(o => o.RestaurantId == query.RestaurantId);

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<OrderStatus>(query.Status, ignoreCase: true, out var status))
                return ApplicationResult<PagedOrders>.Failure(
                    "INVALID_STATUS", $"Status must be one of: {string.Join(", ", Enum.GetNames<OrderStatus>())}.", 400);
            dbQuery = dbQuery.Where(o => o.Status == status);
        }

        var total = await dbQuery.CountAsync(cancellationToken);
        var items = await dbQuery
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new RestaurantOrderSummary(
                o.Id,
                o.Status.ToString(),
                o.FinalFare ?? o.TotalAmount,
                o.PaymentMethod.ToString(),
                o.CreatedAt,
                o.Version))
            .ToListAsync(cancellationToken);

        return ApplicationResult<PagedOrders>.Success(new PagedOrders(items, page, pageSize, total));
    }
}
