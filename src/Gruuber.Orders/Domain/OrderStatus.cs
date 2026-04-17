namespace Gruuber.Orders.Domain;

public enum OrderStatus
{
    Placed,
    Accepted,
    Preparing,
    Ready,
    PickedUp,
    Delivered,
    Cancelled
}
