namespace Gruuber.Orders.Application.Queries;

public record GetOrderQuery(Guid OrderId);
public record OrderResponse(Guid OrderId, Guid RestaurantId, string Status, IList<OrderItemDto> Items);
public record OrderItemDto(Guid MenuItemId, int Quantity, decimal Price);
