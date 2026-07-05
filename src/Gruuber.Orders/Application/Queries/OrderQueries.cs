namespace Gruuber.Orders.Application.Queries;

public record GetOrderQuery(Guid OrderId);
public record OrderResponse(Guid OrderId, Guid RestaurantId, string Status, IList<OrderItemDto> Items);
public record OrderItemDto(Guid MenuItemId, int Quantity, decimal Price);

public record GetOrderItemsQuery(Guid OrderId);

public record GetRestaurantOrdersQuery(Guid RestaurantId, string? Status, int Page, int PageSize);

public record RestaurantOrderSummary(
    Guid OrderId,
    string Status,
    decimal Total,
    string PaymentMethod,
    DateTime CreatedAt,
    long Version);

public record PagedOrders(IReadOnlyList<RestaurantOrderSummary> Items, int Page, int PageSize, int TotalCount);
