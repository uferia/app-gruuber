using Gruuber.SharedKernel.Payments;
using Gruuber.SharedKernel.Pricing;

namespace Gruuber.Orders.Application.Commands;

public record CreateOrderCommand(
    Guid RiderId,
    Guid RestaurantId,
    int RegionId,
    IList<OrderItemRequest> Items,
    double DeliveryLat,
    double DeliveryLng,
    PaymentMethod PaymentMethod);

public record OrderItemRequest(Guid MenuItemId, int Quantity);

public record CreateOrderResponse(Guid OrderId, string Status, Guid PaymentId, decimal Total, FareEstimate? Fare = null);

public record TransitionOrderCommand(
    Guid OrderId,
    string NewStatus,
    long ExpectedVersion,
    int RegionId,
    Guid ActorUserId,
    string ActorRole,
    string? Reason = null,
    string? Note = null);
