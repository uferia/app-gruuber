using Gruuber.Rides.Domain;
using Xunit;

public class RidePoolEntityTests
{
    [Fact]
    public void CreatePool_SetsPoolStatusAndRideType()
    {
        var ride = Ride.CreatePool(Guid.NewGuid(), regionId: 1,
            pickupLat: 51.5, pickupLng: -0.1, destLat: 51.6, destLng: -0.05);

        Assert.Equal(RideStatus.PoolQueued, ride.Status);
        Assert.Equal("pool", ride.RideType);
        Assert.Null(ride.PoolTripId);
        Assert.Null(ride.PoolSlot);
    }

    [Fact]
    public void AssignPool_SetsPoolTripIdAndSlot_AndTransitionsToPoolMatched()
    {
        var ride = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        var tripId = Guid.NewGuid();

        var ok = ride.TryAssignPool(tripId, slot: 1, expectedVersion: 1);

        Assert.True(ok);
        Assert.Equal(tripId, ride.PoolTripId);
        Assert.Equal(1, ride.PoolSlot);
        Assert.Equal(RideStatus.PoolMatched, ride.Status);
        Assert.Equal(2, ride.Version);
    }

    [Fact]
    public void AssignPool_ReturnsFalse_OnVersionMismatch()
    {
        var ride = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        var ok = ride.TryAssignPool(Guid.NewGuid(), slot: 1, expectedVersion: 99);
        Assert.False(ok);
    }

    [Fact]
    public void UpgradeToSolo_TransitionsPoolQueuedToRequested()
    {
        var ride = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        var ok = ride.TryUpgradeToSolo(expectedVersion: 1);

        Assert.True(ok);
        Assert.Equal(RideStatus.Requested, ride.Status);
        Assert.Equal("solo", ride.RideType);
        Assert.Equal(2, ride.Version);
        Assert.Null(ride.PoolTripId);
        Assert.Null(ride.PoolSlot);
    }

    [Fact]
    public void UpgradeToSolo_ReturnsFalse_OnVersionMismatch()
    {
        var ride = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        Assert.False(ride.TryUpgradeToSolo(expectedVersion: 99));
    }

    [Fact]
    public void UpgradeToSolo_ReturnsFalse_WhenNotPoolQueued()
    {
        var ride = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        ride.TryAssignPool(Guid.NewGuid(), slot: 0, expectedVersion: 1); // → PoolMatched
        Assert.False(ride.TryUpgradeToSolo(expectedVersion: 2));
    }

    [Fact]
    public void AssignPool_ReturnsFalse_WhenNotPoolQueued()
    {
        var ride = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        ride.TryAssignPool(Guid.NewGuid(), slot: 0, expectedVersion: 1); // → PoolMatched, Version=2
        Assert.False(ride.TryAssignPool(Guid.NewGuid(), slot: 1, expectedVersion: 2));
    }
}
