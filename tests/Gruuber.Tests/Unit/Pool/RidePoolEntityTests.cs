using Gruuber.Rides.Domain;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class RidePoolEntityTests
{
    [TestMethod]
    public void CreatePool_SetsPoolStatusAndRideType()
    {
        var ride = Ride.CreatePool(Guid.NewGuid(), regionId: 1,
            pickupLat: 51.5, pickupLng: -0.1, destLat: 51.6, destLng: -0.05);

        ride.Status.Should().Be(RideStatus.PoolQueued);
        ride.RideType.Should().Be("pool");
        ride.PoolTripId.Should().BeNull();
        ride.PoolSlot.Should().BeNull();
    }

    [TestMethod]
    public void AssignPool_SetsPoolTripIdAndSlot_AndTransitionsToPoolMatched()
    {
        var ride = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        var tripId = Guid.NewGuid();

        var ok = ride.TryAssignPool(tripId, slot: 1, expectedVersion: 1);

        ok.Should().BeTrue();
        ride.PoolTripId.Should().Be(tripId);
        ride.PoolSlot.Should().Be(1);
        ride.Status.Should().Be(RideStatus.PoolMatched);
        ride.Version.Should().Be(2);
    }

    [TestMethod]
    public void AssignPool_ReturnsFalse_OnVersionMismatch()
    {
        var ride = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        var ok = ride.TryAssignPool(Guid.NewGuid(), slot: 1, expectedVersion: 99);
        ok.Should().BeFalse();
    }

    [TestMethod]
    public void UpgradeToSolo_TransitionsPoolQueuedToRequested()
    {
        var ride = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        var ok = ride.TryUpgradeToSolo(expectedVersion: 1);

        ok.Should().BeTrue();
        ride.Status.Should().Be(RideStatus.Requested);
        ride.RideType.Should().Be("solo");
        ride.Version.Should().Be(2);
        ride.PoolTripId.Should().BeNull();
        ride.PoolSlot.Should().BeNull();
    }

    [TestMethod]
    public void UpgradeToSolo_ReturnsFalse_OnVersionMismatch()
    {
        var ride = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        ride.TryUpgradeToSolo(expectedVersion: 99).Should().BeFalse();
    }

    [TestMethod]
    public void UpgradeToSolo_ReturnsFalse_WhenNotPoolQueued()
    {
        var ride = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        ride.TryAssignPool(Guid.NewGuid(), slot: 0, expectedVersion: 1); // → PoolMatched
        ride.TryUpgradeToSolo(expectedVersion: 2).Should().BeFalse();
    }

    [TestMethod]
    public void AssignPool_ReturnsFalse_WhenNotPoolQueued()
    {
        var ride = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        ride.TryAssignPool(Guid.NewGuid(), slot: 0, expectedVersion: 1); // → PoolMatched, Version=2
        ride.TryAssignPool(Guid.NewGuid(), slot: 1, expectedVersion: 2).Should().BeFalse();
    }
}
