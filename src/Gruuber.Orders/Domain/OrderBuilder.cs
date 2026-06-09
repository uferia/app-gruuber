namespace Gruuber.Orders.Domain;

/// <summary>
/// Builder pattern — constructs an Order aggregate with items using a fluent API.
/// Removes the need for callers to know the exact creation sequence.
/// </summary>
public sealed class OrderBuilder
{
    private Guid _riderId;
    private Guid _restaurantId;
    private Guid _rideId;
    private int _regionId;
    private readonly List<(Guid MenuItemId, int Quantity, decimal Price)> _items = new();

    public OrderBuilder ForRider(Guid riderId)
    {
        _riderId = riderId;
        return this;
    }

    public OrderBuilder FromRestaurant(Guid restaurantId)
    {
        _restaurantId = restaurantId;
        return this;
    }

    public OrderBuilder ForRide(Guid rideId)
    {
        _rideId = rideId;
        return this;
    }

    public OrderBuilder InRegion(int regionId)
    {
        _regionId = regionId;
        return this;
    }

    public OrderBuilder AddItem(Guid menuItemId, int quantity, decimal price)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        if (price < 0) throw new ArgumentOutOfRangeException(nameof(price), "Price cannot be negative.");
        _items.Add((menuItemId, quantity, price));
        return this;
    }

    public Order Build()
    {
        if (_riderId == Guid.Empty) throw new InvalidOperationException("RiderId is required.");
        if (_restaurantId == Guid.Empty) throw new InvalidOperationException("RestaurantId is required.");
        if (_rideId == Guid.Empty) throw new InvalidOperationException("RideId is required.");
        if (_regionId == 0) throw new InvalidOperationException("RegionId is required.");
        if (_items.Count == 0) throw new InvalidOperationException("An order must have at least one item.");

        var order = Order.Create(_riderId, _restaurantId, _rideId, _regionId);
        foreach (var (menuItemId, quantity, price) in _items)
            order.AddItem(menuItemId, quantity, price);

        return order;
    }
}
