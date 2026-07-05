using Gruuber.SharedKernel.Domain;
using Gruuber.SharedKernel.Payments;

namespace Gruuber.Orders.Domain;

public class Order : EntityBase, ISnapshotOriginator<OrderSnapshot>
{
    public Guid RiderId { get; private set; }
    public Guid RestaurantId { get; private set; }
    public Guid? RideId { get; private set; }
    public Guid? DriverId { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.Placed;
    public decimal TotalAmount { get; private set; }
    public decimal? BaseFare { get; private set; }
    public decimal SurgeMultiplier { get; private set; } = 1.0m;
    public decimal? FinalFare { get; private set; }
    public string? SurgeReason { get; private set; }
    public double DeliveryLat { get; private set; }
    public double DeliveryLng { get; private set; }
    public decimal DeliveryFee { get; private set; }
    public PaymentMethod PaymentMethod { get; private set; } = PaymentMethod.CardMock;
    public OrderCancellationReason? CancellationReason { get; private set; }
    public string? CancellationNote { get; private set; }
    public string? CancelledByRole { get; private set; }
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

    public static Order CreateForDelivery(
        Guid riderId,
        Guid restaurantId,
        int regionId,
        double deliveryLat,
        double deliveryLng,
        PaymentMethod paymentMethod)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            RiderId = riderId,
            RestaurantId = restaurantId,
            RideId = null,
            Status = OrderStatus.Placed,
            RegionId = regionId,
            DeliveryLat = deliveryLat,
            DeliveryLng = deliveryLng,
            PaymentMethod = paymentMethod,
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

    public void ApplySurge(decimal baseFare, decimal multiplier, string? reason)
    {
        BaseFare = baseFare;
        SurgeMultiplier = multiplier;
        FinalFare = baseFare * multiplier;
        SurgeReason = reason;
    }

    public void SetDeliveryFee(decimal fee) => DeliveryFee = fee;

    public bool TryCancel(OrderCancellationReason reason, string? note, string cancelledByRole, long expectedVersion)
    {
        if (Version != expectedVersion)
            return false;

        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        CancellationNote = note;
        CancelledByRole = cancelledByRole;
        Version++;
        return true;
    }

    // ── Memento (ISnapshotOriginator) ─────────────────────────────────────────

    public OrderSnapshot CaptureSnapshot() => new()
    {
        EntityId        = Id,
        Version         = Version,
        CapturedAt      = DateTime.UtcNow,
        RiderId         = RiderId,
        RestaurantId    = RestaurantId,
        RideId          = RideId,
        DriverId        = DriverId,
        Status          = Status.ToString(),
        TotalAmount     = TotalAmount,
        FinalFare       = FinalFare,
        SurgeMultiplier = SurgeMultiplier,
        RegionId        = RegionId
    };

    public void RestoreFromSnapshot(OrderSnapshot snapshot)
    {
        DriverId        = snapshot.DriverId;
        Status          = Enum.Parse<OrderStatus>(snapshot.Status);
        TotalAmount     = snapshot.TotalAmount;
        FinalFare       = snapshot.FinalFare;
        SurgeMultiplier = snapshot.SurgeMultiplier;
        Version         = snapshot.Version;
    }
}
