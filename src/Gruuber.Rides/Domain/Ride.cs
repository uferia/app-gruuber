using Gruuber.SharedKernel.Domain;

namespace Gruuber.Rides.Domain;

public class Ride : EntityBase
{
    public Guid RiderId { get; private set; }
    public Guid? DriverId { get; private set; }
    public RideStatus Status { get; private set; } = RideStatus.Requested;
    public string RideType { get; private set; } = string.Empty;
    public double PickupLat { get; private set; }
    public double PickupLng { get; private set; }
    public double? DestLat { get; private set; }
    public double? DestLng { get; private set; }
    public decimal? BaseFare { get; private set; }
    public decimal SurgeMultiplier { get; private set; } = 1.0m;
    public decimal? FinalFare { get; private set; }
    public string? SurgeReason { get; private set; }

    // Pool-specific properties
    public Guid? PoolTripId { get; private set; }
    public int? PoolSlot { get; private set; }

    private Ride() { }

    public static Ride Create(
        Guid riderId,
        string rideType,
        int regionId,
        double pickupLat,
        double pickupLng,
        double? destLat = null,
        double? destLng = null,
        decimal? baseFare = null,
        decimal surgeMultiplier = 1.0m,
        decimal? finalFare = null,
        string? surgeReason = null)
    {
        return new Ride
        {
            Id = Guid.NewGuid(),
            RiderId = riderId,
            RideType = rideType,
            Status = RideStatus.Requested,
            RegionId = regionId,
            PickupLat = pickupLat,
            PickupLng = pickupLng,
            DestLat = destLat,
            DestLng = destLng,
            BaseFare = baseFare,
            SurgeMultiplier = surgeMultiplier,
            FinalFare = finalFare,
            SurgeReason = surgeReason,
            CreatedAt = DateTime.UtcNow,
            Version = 1
        };
    }

    /// <summary>Creates a pool ride in PoolQueued status.</summary>
    public static Ride CreatePool(
        Guid riderId, int regionId,
        double pickupLat, double pickupLng,
        double destLat, double destLng,
        decimal? baseFare = null, decimal surgeMultiplier = 1.0m, decimal? finalFare = null)
    {
        return new Ride
        {
            Id = Guid.NewGuid(),
            RiderId = riderId,
            RideType = "pool",
            Status = RideStatus.PoolQueued,
            RegionId = regionId,
            PickupLat = pickupLat,
            PickupLng = pickupLng,
            DestLat = destLat,
            DestLng = destLng,
            BaseFare = baseFare,
            SurgeMultiplier = surgeMultiplier,
            FinalFare = finalFare,
            CreatedAt = DateTime.UtcNow,
            Version = 1
        };
    }

    public bool TryMatch(Guid driverId, long expectedVersion)
    {
        if (Version != expectedVersion || Status != RideStatus.Requested)
            return false;

        DriverId = driverId;
        Status = RideStatus.Matched;
        Version++;
        return true;
    }

    /// <summary>
    /// Transitions PoolQueued → PoolMatched and assigns pool trip.
    /// Returns false on version mismatch (caller should retry with fresh version).
    /// </summary>
    public bool TryAssignPool(Guid poolTripId, int slot, long expectedVersion)
    {
        if (Version != expectedVersion || Status != RideStatus.PoolQueued)
            return false;

        PoolTripId = poolTripId;
        PoolSlot = slot;
        Status = RideStatus.PoolMatched;
        Version++;
        return true;
    }

    public bool TryTransition(RideStatus next, long expectedVersion)
    {
        if (Version != expectedVersion)
            return false;

        Status = next;
        Version++;
        return true;
    }

    /// <summary>
    /// Upgrades a timed-out PoolQueued ride to solo Requested.
    /// Returns false on version mismatch.
    /// </summary>
    public bool TryUpgradeToSolo(long expectedVersion)
    {
        if (Version != expectedVersion || Status != RideStatus.PoolQueued)
            return false;

        RideType = "solo";
        Status = RideStatus.Requested;
        PoolTripId = null;
        PoolSlot = null;
        Version++;
        return true;
    }

    /// <summary>Cancels a timed-out pool ride. Called by PoolTimeoutWorker.</summary>
    public void CancelExpiredPool()
    {
        if (Status != RideStatus.PoolQueued) return;
        Status = RideStatus.Cancelled;
        Version++;
    }
}
