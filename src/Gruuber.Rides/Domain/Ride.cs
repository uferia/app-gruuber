using Gruuber.SharedKernel.Domain;

namespace Gruuber.Rides.Domain;

public class Ride : EntityBase
{
    public Guid RiderId { get; private set; }
    public Guid? DriverId { get; private set; }
    public RideStatus Status { get; private set; } = RideStatus.Requested;
    public string RideType { get; private set; } = string.Empty;

    private Ride() { }

    public static Ride Create(Guid riderId, string rideType, int regionId)
    {
        return new Ride
        {
            Id = Guid.NewGuid(),
            RiderId = riderId,
            RideType = rideType,
            Status = RideStatus.Requested,
            RegionId = regionId,
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

    public bool TryTransition(RideStatus next, long expectedVersion)
    {
        if (Version != expectedVersion)
            return false;

        Status = next;
        Version++;
        return true;
    }
}
