using Gruuber.Orders.Domain;
using Gruuber.Rides.Domain;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gruuber.Tests.Unit.Patterns;

/// <summary>
/// Tests for the Memento pattern on Ride and Order aggregates.
/// Verifies CaptureSnapshot captures the correct state, and RestoreFromSnapshot
/// rolls back to the captured state without affecting other fields.
/// </summary>
[TestClass]
public class MementoTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // RideSnapshot (Originator = Ride)
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void Ride_CaptureSnapshot_ContainsCurrentState()
    {
        // Arrange
        var riderId = Guid.NewGuid();
        var ride = new RideBuilder()
            .ForRider(riderId)
            .InRegion(1)
            .FromPickup(40.7, -74.0)
            .ToDestination(40.8, -73.9)
            .WithFare(10m, 1.5m, "demand")
            .Build();

        // Act
        var snapshot = ride.CaptureSnapshot();

        // Assert
        snapshot.EntityId.Should().Be(ride.Id);
        snapshot.Version.Should().Be(ride.Version);
        snapshot.RiderId.Should().Be(ride.RiderId);
        snapshot.Status.Should().Be("Requested");
        snapshot.RideType.Should().Be("solo");
        snapshot.PickupLat.Should().BeApproximately(40.7, 0.001);
        snapshot.PickupLng.Should().BeApproximately(-74.0, 0.001);
        snapshot.FinalFare.Should().Be(15m);
        snapshot.SurgeMultiplier.Should().Be(1.5m);
        snapshot.RegionId.Should().Be(1);
        snapshot.DriverId.Should().BeNull();
    }

    [TestMethod]
    public void Ride_CaptureSnapshot_AfterMatch_ReflectsDriverAndStatus()
    {
        // Arrange
        var ride     = new RideBuilder().ForRider(Guid.NewGuid()).InRegion(1).FromPickup(0, 0).Build();
        var driverId = Guid.NewGuid();
        ride.TryMatch(driverId, expectedVersion: 1);

        // Act
        var snapshot = ride.CaptureSnapshot();

        // Assert
         snapshot.Status.Should().Be("Matched");
          snapshot.DriverId.Should().Be(driverId);
                 snapshot.Version.Should().Be(2);
    }

    [TestMethod]
    public void Ride_RestoreFromSnapshot_RollsBackStatus()
    {
        // Arrange
        var ride     = new RideBuilder().ForRider(Guid.NewGuid()).InRegion(1).FromPickup(0, 0).Build();
        var snapshot = ride.CaptureSnapshot(); // capture at Requested / version 1

        // Move the ride forward
        var driverId = Guid.NewGuid();
        ride.TryMatch(driverId, expectedVersion: 1);
        ride.Status.Should().Be(RideStatus.Matched);

        // Act — restore to the captured state
        ride.RestoreFromSnapshot(snapshot);

        // Assert
        ride.Status.Should().Be(RideStatus.Requested);
        ride.DriverId.Should().BeNull();
        ride.Version.Should().Be(1);
    }

    [TestMethod]
    public void Ride_CaptureSnapshot_CapturedAtIsRecent()
    {
        // Arrange
        var ride     = new RideBuilder().ForRider(Guid.NewGuid()).InRegion(1).FromPickup(0, 0).Build();
        var before   = DateTime.UtcNow.AddSeconds(-1);

        // Act
        var snapshot = ride.CaptureSnapshot();
        var after    = DateTime.UtcNow.AddSeconds(1);

        // Assert
        (snapshot.CapturedAt >= before && snapshot.CapturedAt <= after).Should().BeTrue();
    }

    [TestMethod]
    public void Ride_MultipleSnapshots_AreIndependent()
    {
        // Arrange
        var ride     = new RideBuilder().ForRider(Guid.NewGuid()).InRegion(1).FromPickup(0, 0).Build();
        var snap1    = ride.CaptureSnapshot(); // version 1

        ride.TryMatch(Guid.NewGuid(), 1);
        var snap2 = ride.CaptureSnapshot(); // version 2

        // Assert — snapshots do not share state
                snap1.Version.Should().Be(1);
                snap2.Version.Should().Be(2);
        snap1.Status.Should().Be("Requested");
          snap2.Status.Should().Be("Matched");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // OrderSnapshot (Originator = Order)
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void Order_CaptureSnapshot_ContainsCurrentState()
    {
        // Arrange
        var riderId      = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var rideId       = Guid.NewGuid();
        var order = new OrderBuilder()
            .ForRider(riderId)
            .FromRestaurant(restaurantId)
            .ForRide(rideId)
            .InRegion(2)
            .AddItem(Guid.NewGuid(), 2, 7.50m)
            .Build();

        // Act
        var snapshot = order.CaptureSnapshot();

        // Assert
                snapshot.EntityId.Should().Be(order.Id);
           snapshot.Version.Should().Be(order.Version);
                 snapshot.RiderId.Should().Be(riderId);
            snapshot.RestaurantId.Should().Be(restaurantId);
                  snapshot.RideId.Should().Be(rideId);
                snapshot.Status.Should().Be("Placed");
                     snapshot.TotalAmount.Should().Be(15m);
                       snapshot.RegionId.Should().Be(2);
        snapshot.DriverId.Should().BeNull();
    }

    [TestMethod]
    public void Order_CaptureSnapshot_AfterTransition_ReflectsNewStatus()
    {
        // Arrange
        var order = new OrderBuilder()
            .ForRider(Guid.NewGuid()).FromRestaurant(Guid.NewGuid())
            .ForRide(Guid.NewGuid()).InRegion(1)
            .AddItem(Guid.NewGuid(), 1, 5m).Build();
        order.TryTransition(OrderStatus.Accepted, expectedVersion: 1);

        // Act
        var snapshot = order.CaptureSnapshot();

        // Assert
        snapshot.Status.Should().Be("Accepted");
                 snapshot.Version.Should().Be(2);
    }

    [TestMethod]
    public void Order_RestoreFromSnapshot_RollsBackStatusAndVersion()
    {
        // Arrange
        var order    = new OrderBuilder()
            .ForRider(Guid.NewGuid()).FromRestaurant(Guid.NewGuid())
            .ForRide(Guid.NewGuid()).InRegion(1)
            .AddItem(Guid.NewGuid(), 1, 5m).Build();
        var snapshot = order.CaptureSnapshot(); // Placed / version 1

        order.TryTransition(OrderStatus.Accepted, 1);
        order.TryTransition(OrderStatus.Preparing, 2);

        // Act — restore
        order.RestoreFromSnapshot(snapshot);

        // Assert
        order.Status.Should().Be(OrderStatus.Placed);
        order.Version.Should().Be(1);
    }

    [TestMethod]
    public void Order_CaptureSnapshot_AfterSurge_ReflectsFare()
    {
        // Arrange
        var order = new OrderBuilder()
            .ForRider(Guid.NewGuid()).FromRestaurant(Guid.NewGuid())
            .ForRide(Guid.NewGuid()).InRegion(3)
            .AddItem(Guid.NewGuid(), 1, 20m).Build();
        order.ApplySurge(baseFare: 20m, multiplier: 2m, reason: "peak_hour");

        // Act
        var snapshot = order.CaptureSnapshot();

        // Assert
        snapshot.FinalFare.Should().Be(40m);
         snapshot.SurgeMultiplier.Should().Be(2m);
    }
}
