using Gruuber.Orders.Domain;
using Gruuber.Rides.Domain;
using FluentAssertions;
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
        ride.RiderId.Should().Be(riderId);
        ride.RideType.Should().Be("solo");
        ride.RegionId.Should().Be(1);
        ride.Status.Should().Be(RideStatus.Requested);
        ride.Version.Should().Be(1);
        ride.SurgeMultiplier.Should().Be(1.0m);
        ride.DriverId.Should().BeNull();
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
        ride.BaseFare.Should().Be(10m);
        ride.SurgeMultiplier.Should().Be(1.5m);
        ride.FinalFare.Should().Be(15m);
        ride.SurgeReason.Should().Be("demand");
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
        ride.DestLat.Should().Be(40.8);
        ride.DestLng.Should().Be(-73.9);
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
        ride.Status.Should().Be(RideStatus.PoolQueued);
        ride.RideType.Should().Be("pool");
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
        order.Status.Should().Be(OrderStatus.Placed);
        order.Items.Count.Should().Be(2);
        order.TotalAmount.Should().Be(22.50m); // 2*5 + 1*12.5
        order.RegionId.Should().Be(1);
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
        order.TotalAmount.Should().Be(27.00m);
    }
}
