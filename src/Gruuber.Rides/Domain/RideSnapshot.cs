using Gruuber.SharedKernel.Domain;

namespace Gruuber.Rides.Domain;

/// <summary>
/// Memento — immutable snapshot of a Ride's auditable state.
/// Stored in the ride_snapshots table to support audit trail and rollback.
/// </summary>
public sealed record RideSnapshot : ISnapshot<Guid>
{
    public Guid EntityId { get; init; }
    public long Version { get; init; }
    public DateTime CapturedAt { get; init; } = DateTime.UtcNow;
    public Guid RiderId { get; init; }
    public Guid? DriverId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string RideType { get; init; } = string.Empty;
    public double PickupLat { get; init; }
    public double PickupLng { get; init; }
    public double? DestLat { get; init; }
    public double? DestLng { get; init; }
    public decimal? FinalFare { get; init; }
    public decimal SurgeMultiplier { get; init; }
    public int RegionId { get; init; }
}
