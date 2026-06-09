using Gruuber.Orders.Domain;
using Gruuber.Rides.Domain;
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
        Assert.AreEqual(ride.Id,              snapshot.EntityId);
        Assert.AreEqual(ride.Version,         snapshot.Version);
        Assert.AreEqual(ride.RiderId,         snapshot.RiderId);
        Assert.AreEqual("Requested",          snapshot.Status);
        Assert.AreEqual("solo",               snapshot.RideType);
        Assert.AreEqual(40.7,                 snapshot.PickupLat, delta: 0.001);
        Assert.AreEqual(-74.0,                snapshot.PickupLng, delta: 0.001);
        Assert.AreEqual(15m,                  snapshot.FinalFare);
        Assert.AreEqual(1.5m,                 snapshot.SurgeMultiplier);
        Assert.AreEqual(1,                    snapshot.RegionId);
        Assert.IsNull(snapshot.DriverId);
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
        Assert.AreEqual("Matched",  snapshot.Status);
        Assert.AreEqual(driverId,   snapshot.DriverId);
        Assert.AreEqual(2,          snapshot.Version);
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
        Assert.AreEqual(RideStatus.Matched, ride.Status);

        // Act — restore to the captured state
        ride.RestoreFromSnapshot(snapshot);

        // Assert
        Assert.AreEqual(RideStatus.Requested, ride.Status);
        Assert.IsNull(ride.DriverId);
        Assert.AreEqual(1, ride.Version);
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
        Assert.IsTrue(snapshot.CapturedAt >= before && snapshot.CapturedAt <= after);
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
        Assert.AreEqual(1,         snap1.Version);
        Assert.AreEqual(2,         snap2.Version);
        Assert.AreEqual("Requested", snap1.Status);
        Assert.AreEqual("Matched",   snap2.Status);
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
        Assert.AreEqual(order.Id,         snapshot.EntityId);
        Assert.AreEqual(order.Version,    snapshot.Version);
        Assert.AreEqual(riderId,          snapshot.RiderId);
        Assert.AreEqual(restaurantId,     snapshot.RestaurantId);
        Assert.AreEqual(rideId,           snapshot.RideId);
        Assert.AreEqual("Placed",         snapshot.Status);
        Assert.AreEqual(15m,              snapshot.TotalAmount);
        Assert.AreEqual(2,                snapshot.RegionId);
        Assert.IsNull(snapshot.DriverId);
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
        Assert.AreEqual("Accepted", snapshot.Status);
        Assert.AreEqual(2,          snapshot.Version);
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
        Assert.AreEqual(OrderStatus.Placed, order.Status);
        Assert.AreEqual(1, order.Version);
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
        Assert.AreEqual(40m, snapshot.FinalFare);
        Assert.AreEqual(2m,  snapshot.SurgeMultiplier);
    }
}
