using Gruuber.SharedKernel.Domain;

namespace Gruuber.Orders.Domain;

/// <summary>
/// Memento — immutable snapshot of an Order's auditable state.
/// </summary>
public sealed record OrderSnapshot : ISnapshot<Guid>
{
    public Guid EntityId { get; init; }
    public long Version { get; init; }
    public DateTime CapturedAt { get; init; } = DateTime.UtcNow;
    public Guid RiderId { get; init; }
    public Guid RestaurantId { get; init; }
    public Guid RideId { get; init; }
    public Guid? DriverId { get; init; }
    public string Status { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public decimal? FinalFare { get; init; }
    public decimal SurgeMultiplier { get; init; }
    public int RegionId { get; init; }
}
