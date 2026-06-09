using Gruuber.Orders.Domain;
using Gruuber.Rides.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gruuber.Tests.Unit.Patterns;

/// <summary>
/// Tests for the Builder pattern on RideBuilder and OrderBuilder.
/// Covers happy paths, optional fields, guard validations and pool-specific construction.
/// </summary>
[TestClass]
public class BuilderTests
{
    // ── RideBuilder ───────────────────────────────────────────────────────────

    [TestMethod]
    public void RideBuilder_Build_SoloRide_HasCorrectDefaults()
    {
        // Arrange
        var riderId = Guid.NewGuid();

        // Act
        var ride = new RideBuilder()
            .ForRider(riderId)
            .WithType("solo")
            .InRegion(1)
            .FromPickup(40.758, -73.985)
            .Build();

        // Assert
        Assert.AreEqual(riderId, ride.RiderId);
        Assert.AreEqual("solo", ride.RideType);
        Assert.AreEqual(1, ride.RegionId);
        Assert.AreEqual(RideStatus.Requested, ride.Status);
        Assert.AreEqual(1, ride.Version);
        Assert.AreEqual(1.0m, ride.SurgeMultiplier);
        Assert.IsNull(ride.DriverId);
    }

    [TestMethod]
    public void RideBuilder_Build_WithFare_SetsAllFareFields()
    {
        // Arrange / Act
        var ride = new RideBuilder()
            .ForRider(Guid.NewGuid())
            .InRegion(2)
            .FromPickup(51.5, -0.1)
            .WithFare(baseFare: 10m, surgeMultiplier: 1.5m, surgeReason: "demand")
            .Build();

        // Assert
        Assert.AreEqual(10m, ride.BaseFare);
        Assert.AreEqual(1.5m, ride.SurgeMultiplier);
        Assert.AreEqual(15m, ride.FinalFare);
        Assert.AreEqual("demand", ride.SurgeReason);
    }

    [TestMethod]
    public void RideBuilder_Build_WithDestination_SetsDestinationCoordinates()
    {
        // Arrange / Act
        var ride = new RideBuilder()
            .ForRider(Guid.NewGuid())
            .InRegion(1)
            .FromPickup(40.7, -74.0)
            .ToDestination(40.8, -73.9)
            .Build();

        // Assert
        Assert.AreEqual(40.8, ride.DestLat);
        Assert.AreEqual(-73.9, ride.DestLng);
    }

    [TestMethod]
    public void RideBuilder_Build_PoolRide_HasPoolQueuedStatus()
    {
        // Arrange / Act
        var ride = new RideBuilder()
            .ForRider(Guid.NewGuid())
            .WithType("pool")
            .InRegion(1)
            .FromPickup(40.7, -74.0)
            .ToDestination(40.8, -73.9)
            .Build();

        // Assert
        Assert.AreEqual(RideStatus.PoolQueued, ride.Status);
        Assert.AreEqual("pool", ride.RideType);
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidOperationException))]
    public void RideBuilder_Build_MissingRiderId_ThrowsInvalidOperationException()
    {
        // Arrange / Act — missing ForRider()
        new RideBuilder()
            .InRegion(1)
            .FromPickup(0, 0)
            .Build();
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidOperationException))]
    public void RideBuilder_Build_MissingRegion_ThrowsInvalidOperationException()
    {
        // Arrange / Act — missing InRegion()
        new RideBuilder()
            .ForRider(Guid.NewGuid())
            .FromPickup(0, 0)
            .Build();
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidOperationException))]
    public void RideBuilder_Build_PoolRideMissingDestination_Throws()
    {
        // Arrange / Act — pool ride without ToDestination()
        new RideBuilder()
            .ForRider(Guid.NewGuid())
            .WithType("pool")
            .InRegion(1)
            .FromPickup(40.7, -74.0)
            .Build(); // must throw — dest required for pool
    }

    // ── OrderBuilder ──────────────────────────────────────────────────────────

    [TestMethod]
    public void OrderBuilder_Build_WithItems_ReturnsOrderWithCorrectTotal()
    {
        // Arrange
        var riderId       = Guid.NewGuid();
        var restaurantId  = Guid.NewGuid();
        var rideId        = Guid.NewGuid();

        // Act
        var order = new OrderBuilder()
            .ForRider(riderId)
            .FromRestaurant(restaurantId)
            .ForRide(rideId)
            .InRegion(1)
            .AddItem(Guid.NewGuid(), quantity: 2, price: 5.00m)
            .AddItem(Guid.NewGuid(), quantity: 1, price: 12.50m)
            .Build();

        // Assert
        Assert.AreEqual(OrderStatus.Placed, order.Status);
        Assert.AreEqual(2, order.Items.Count);
        Assert.AreEqual(22.50m, order.TotalAmount); // 2*5 + 1*12.5
        Assert.AreEqual(1, order.RegionId);
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidOperationException))]
    public void OrderBuilder_Build_NoItems_ThrowsInvalidOperationException()
    {
        // Arrange / Act — no AddItem() calls
        new OrderBuilder()
            .ForRider(Guid.NewGuid())
            .FromRestaurant(Guid.NewGuid())
            .ForRide(Guid.NewGuid())
            .InRegion(1)
            .Build();
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentOutOfRangeException))]
    public void OrderBuilder_AddItem_ZeroQuantity_Throws()
    {
        // Arrange / Act
        new OrderBuilder()
            .ForRider(Guid.NewGuid())
            .FromRestaurant(Guid.NewGuid())
            .ForRide(Guid.NewGuid())
            .InRegion(1)
            .AddItem(Guid.NewGuid(), quantity: 0, price: 5.00m); // must throw
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentOutOfRangeException))]
    public void OrderBuilder_AddItem_NegativePrice_Throws()
    {
        // Arrange / Act
        new OrderBuilder()
            .ForRider(Guid.NewGuid())
            .FromRestaurant(Guid.NewGuid())
            .ForRide(Guid.NewGuid())
            .InRegion(1)
            .AddItem(Guid.NewGuid(), quantity: 1, price: -1.00m); // must throw
    }

    [TestMethod]
    public void OrderBuilder_Build_MultipleItems_ComputesSubtotalsCorrectly()
    {
        // Arrange
        var item1 = Guid.NewGuid();
        var item2 = Guid.NewGuid();

        // Act
        var order = new OrderBuilder()
            .ForRider(Guid.NewGuid())
            .FromRestaurant(Guid.NewGuid())
            .ForRide(Guid.NewGuid())
            .InRegion(3)
            .AddItem(item1, quantity: 3, price: 4.00m)  // subtotal = 12
            .AddItem(item2, quantity: 2, price: 7.50m)  // subtotal = 15
            .Build();

        // Assert
        Assert.AreEqual(27.00m, order.TotalAmount);
    }
}
