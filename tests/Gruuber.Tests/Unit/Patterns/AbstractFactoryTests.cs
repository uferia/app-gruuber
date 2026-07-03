using Gruuber.Orders.Application;
using Gruuber.Rides.Application;
using Gruuber.SharedKernel.Messaging;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace Gruuber.Tests.Unit.Patterns;

/// <summary>
/// Tests for the Abstract Factory pattern — IEventMessageFactory&lt;TOutboxEntry&gt;.
/// Verifies that RideOutboxFactory and OrderOutboxFactory produce correctly structured
/// outbox entries with the right topic keys and JSON payloads.
/// </summary>
[TestClass]
public class AbstractFactoryTests
{
    // ── RideOutboxFactory ─────────────────────────────────────────────────────

    [TestMethod]
    public void RideOutboxFactory_CreateRideRequested_ProducesCorrectEventType()
    {
        // Arrange
        var factory = new RideOutboxFactory();
        var rideId  = Guid.NewGuid();

        // Act
        var entry = factory.CreateRideRequested(
            regionId: 1, rideId: rideId, riderId: Guid.NewGuid(),
            pickupLat: 40.7, pickupLng: -74.0, surgeMultiplier: 1.5m, finalFare: 15m);

        // Assert
        entry.EventType.Should().Be("ride-events-1");
        entry.Status.Should().Be("pending");
        entry.Id.Should().NotBe(Guid.Empty);
    }

    [TestMethod]
    public void RideOutboxFactory_CreateRideRequested_PayloadContainsRideId()
    {
        // Arrange
        var factory = new RideOutboxFactory();
        var rideId  = Guid.NewGuid();

        // Act
        var entry = factory.CreateRideRequested(1, rideId, Guid.NewGuid(), 0, 0, 1m, 10m);
        var doc   = JsonDocument.Parse(entry.Payload).RootElement;

        // Assert
        doc.GetProperty("RideId").GetString().Should().Be(rideId.ToString());
        doc.GetProperty("EventName").GetString().Should().Be("ride_requested");
    }

    [TestMethod]
    public void RideOutboxFactory_CreateDriverMatched_ContainsDriverIdAndScore()
    {
        // Arrange
        var factory   = new RideOutboxFactory();
        var driverId  = Guid.NewGuid();

        // Act
        var entry = factory.CreateDriverMatched(regionId: 2, rideId: Guid.NewGuid(), driverId, score: 0.87);
        var doc   = JsonDocument.Parse(entry.Payload).RootElement;

        // Assert
        entry.EventType.Should().Be("ride-events-2");
        doc.GetProperty("DriverId").GetString().Should().Be(driverId.ToString());
        doc.GetProperty("Score").GetDouble().Should().BeApproximately(0.87, 0.001);
    }

    [TestMethod]
    public void RideOutboxFactory_CreateRideStatusChanged_ContainsNewStatus()
    {
        // Arrange
        var factory = new RideOutboxFactory();

        // Act
        var entry = factory.CreateRideStatusChanged(1, Guid.NewGuid(), "Completed", Guid.NewGuid());
        var doc   = JsonDocument.Parse(entry.Payload).RootElement;

        // Assert
        doc.GetProperty("NewStatus").GetString().Should().Be("Completed");
        doc.GetProperty("EventName").GetString().Should().Be("ride_status_changed");
    }

    [TestMethod]
    public void RideOutboxFactory_CreateFailed_GenericInterface_ProducesRideFailedEvent()
    {
        // Arrange — use via the abstract interface
        IEventMessageFactory<Gruuber.Rides.Infrastructure.RideOutboxEntry> factory = new RideOutboxFactory();
        var entityId = Guid.NewGuid();

        // Act
        var entry = factory.CreateFailed(regionId: 3, entityId, "timeout");
        var doc   = JsonDocument.Parse(entry.Payload).RootElement;

        // Assert
        entry.EventType.Should().Be("ride-events-3");
        doc.GetProperty("EventName").GetString().Should().Be("ride_failed");
        doc.GetProperty("Reason").GetString().Should().Be("timeout");
    }

    [TestMethod]
    public void RideOutboxFactory_RegionId_IsEmbeddedInTopic()
    {
        // Arrange
        var factory = new RideOutboxFactory();

        // Act — different region IDs
        var region5  = factory.CreateRideRequested(5, Guid.NewGuid(), Guid.NewGuid(), 0, 0, 1m, 5m);
        var region99 = factory.CreateRideRequested(99, Guid.NewGuid(), Guid.NewGuid(), 0, 0, 1m, 5m);

        // Assert
        region5.EventType.Should().Be("ride-events-5");
        region99.EventType.Should().Be("ride-events-99");
    }

    // ── OrderOutboxFactory ────────────────────────────────────────────────────

    [TestMethod]
    public void OrderOutboxFactory_CreateOrderCreated_ProducesCorrectEventType()
    {
        // Arrange
        var factory = new OrderOutboxFactory();
        var orderId = Guid.NewGuid();

        // Act
        var entry = factory.CreateOrderCreated(
            regionId: 1, orderId, riderId: Guid.NewGuid(),
            restaurantId: Guid.NewGuid(), rideId: Guid.NewGuid(),
            surgeMultiplier: 1.0m, finalFare: 20m);

        // Assert
        entry.EventType.Should().Be("order-events-1");
        entry.Status.Should().Be("pending");
    }

    [TestMethod]
    public void OrderOutboxFactory_CreateOrderCreated_PayloadContainsOrderId()
    {
        // Arrange
        var factory = new OrderOutboxFactory();
        var orderId = Guid.NewGuid();

        // Act
        var entry = factory.CreateOrderCreated(1, orderId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1m, 20m);
        var doc   = JsonDocument.Parse(entry.Payload).RootElement;

        // Assert
        doc.GetProperty("OrderId").GetString().Should().Be(orderId.ToString());
        doc.GetProperty("EventName").GetString().Should().Be("order_created");
    }

    [TestMethod]
    public void OrderOutboxFactory_CreateOrderStatusChanged_ContainsActorId()
    {
        // Arrange
        var factory = new OrderOutboxFactory();
        var actorId = Guid.NewGuid();

        // Act
        var entry = factory.CreateOrderStatusChanged(1, Guid.NewGuid(), "Preparing", actorId);
        var doc   = JsonDocument.Parse(entry.Payload).RootElement;

        // Assert
        doc.GetProperty("NewStatus").GetString().Should().Be("Preparing");
        doc.GetProperty("ActorId").GetString().Should().Be(actorId.ToString());
    }

    [TestMethod]
    public void OrderOutboxFactory_CreateOrderCancelled_SetsRefundRequired()
    {
        // Arrange
        var factory = new OrderOutboxFactory();

        // Act
        var entry = factory.CreateOrderCancelled(1, Guid.NewGuid(), "restaurant_closed");
        var doc   = JsonDocument.Parse(entry.Payload).RootElement;

        // Assert
        doc.GetProperty("RefundRequired").GetBoolean().Should().BeTrue();
        doc.GetProperty("EventName").GetString().Should().Be("order_cancelled");
    }

    [TestMethod]
    public void OrderOutboxFactory_CreateFailed_GenericInterface_ProducesOrderFailedEvent()
    {
        // Arrange — use via the abstract interface
        IEventMessageFactory<Gruuber.Orders.Infrastructure.OrderOutboxEntry> factory = new OrderOutboxFactory();

        // Act
        var entry = factory.CreateFailed(regionId: 2, Guid.NewGuid(), "payment_timeout");
        var doc   = JsonDocument.Parse(entry.Payload).RootElement;

        // Assert
        doc.GetProperty("EventName").GetString().Should().Be("order_failed");
        doc.GetProperty("Reason").GetString().Should().Be("payment_timeout");
    }
}
