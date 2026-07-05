using Gruuber.SharedKernel.Domain;
using Gruuber.SharedKernel.Payments;

namespace Gruuber.Payments.Domain;

public class Payment : EntityBase
{
    public Guid? RideId { get; private set; }
    public Guid? OrderId { get; private set; }
    public Guid RiderId { get; private set; }
    public string Currency { get; private set; } = "USD";
    public PaymentStatus Status { get; private set; } = PaymentStatus.Initiated;
    public PaymentMethod Method { get; private set; } = PaymentMethod.CardMock;
    public decimal Amount { get; private set; }
    public DateTime? ConfirmedAt { get; private set; }
    public int PollingAttempts { get; private set; }

    private Payment() { }

    public static Payment Create(Guid rideId, Guid riderId, decimal amount, string currency)
    {
        return new Payment
        {
            Id = Guid.NewGuid(),
            RideId = rideId,
            RiderId = riderId,
            Amount = amount,
            Currency = currency,
            Status = PaymentStatus.Initiated,
            CreatedAt = DateTime.UtcNow,
            Version = 1
        };
    }

    public static Payment CreateForOrder(Guid orderId, Guid riderId, decimal amount, string currency, PaymentMethod method, int regionId)
    {
        return new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            RiderId = riderId,
            Amount = amount,
            Currency = currency,
            Method = method,
            RegionId = regionId,
            Status = PaymentStatus.Initiated,
            CreatedAt = DateTime.UtcNow,
            Version = 1
        };
    }

    public bool TryConfirm(long expectedVersion)
    {
        if (Version != expectedVersion) return false;
        Status = PaymentStatus.Succeeded;
        ConfirmedAt = DateTime.UtcNow;
        Version++;
        return true;
    }

    public bool TryFail(long expectedVersion)
    {
        if (Version != expectedVersion) return false;
        Status = PaymentStatus.Failed;
        Version++;
        return true;
    }

    public bool TryTimeout(long expectedVersion)
    {
        if (Version != expectedVersion) return false;
        Status = PaymentStatus.FailedTimeout;
        Version++;
        return true;
    }

    public void IncrementPollingAttempt() => PollingAttempts++;
}
