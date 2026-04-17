using Gruuber.SharedKernel.Domain;

namespace Gruuber.Orders.Domain;

public class Order : EntityBase
{
    public Guid RiderId { get; private set; }
    public Guid RestaurantId { get; private set; }
    public Guid RideId { get; private set; }
    public Guid? DriverId { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.Placed;
    public decimal TotalAmount { get; private set; }
    private readonly List<OrderItem> _items = new();
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    private Order() { }

    public static Order Create(Guid riderId, Guid restaurantId, Guid rideId, int regionId)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            RiderId = riderId,
            RestaurantId = restaurantId,
            RideId = rideId,
            Status = OrderStatus.Placed,
            RegionId = regionId,
            CreatedAt = DateTime.UtcNow,
            Version = 1
        };
    }

    public void AddItem(Guid menuItemId, int quantity, decimal price)
    {
        var item = OrderItem.Create(Id, menuItemId, quantity, price);
        _items.Add(item);
        TotalAmount += item.Subtotal;
    }

    public bool TryTransition(OrderStatus next, long expectedVersion)
    {
        if (Version != expectedVersion)
            return false;

        Status = next;
        Version++;
        return true;
    }

    public bool TryAssignDriver(Guid driverId, long expectedVersion)
    {
        if (Version != expectedVersion)
            return false;

        DriverId = driverId;
        Version++;
        return true;
    }
}
