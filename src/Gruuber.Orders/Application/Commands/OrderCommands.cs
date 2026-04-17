namespace Gruuber.Orders.Application.Commands;

public record CreateOrderCommand(Guid RiderId, Guid RestaurantId, Guid RideId, int RegionId, IList<OrderItemRequest> Items);
public record OrderItemRequest(Guid MenuItemId, int Quantity, decimal Price);
public record CreateOrderResponse(Guid OrderId, string Status);

public record TransitionOrderCommand(Guid OrderId, string NewStatus, long ExpectedVersion, int RegionId);
