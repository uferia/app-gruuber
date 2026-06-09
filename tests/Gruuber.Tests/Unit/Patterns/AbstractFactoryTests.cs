using Gruuber.Orders.Application;
using Gruuber.Rides.Application;
using Gruuber.SharedKernel.Messaging;
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
        Assert.AreEqual("ride-events-1", entry.EventType);
        Assert.AreEqual("pending", entry.Status);
        Assert.AreNotEqual(Guid.Empty, entry.Id);
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
        Assert.AreEqual(rideId.ToString(), doc.GetProperty("RideId").GetString());
        Assert.AreEqual("ride_requested", doc.GetProperty("EventName").GetString());
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
        Assert.AreEqual("ride-events-2", entry.EventType);
        Assert.AreEqual(driverId.ToString(), doc.GetProperty("DriverId").GetString());
        Assert.AreEqual(0.87, doc.GetProperty("Score").GetDouble(), delta: 0.001);
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
        Assert.AreEqual("Completed", doc.GetProperty("NewStatus").GetString());
        Assert.AreEqual("ride_status_changed", doc.GetProperty("EventName").GetString());
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
        Assert.AreEqual("ride-events-3", entry.EventType);
        Assert.AreEqual("ride_failed", doc.GetProperty("EventName").GetString());
        Assert.AreEqual("timeout", doc.GetProperty("Reason").GetString());
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
        Assert.AreEqual("ride-events-5", region5.EventType);
        Assert.AreEqual("ride-events-99", region99.EventType);
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
        Assert.AreEqual("order-events-1", entry.EventType);
        Assert.AreEqual("pending", entry.Status);
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
        Assert.AreEqual(orderId.ToString(), doc.GetProperty("OrderId").GetString());
        Assert.AreEqual("order_created", doc.GetProperty("EventName").GetString());
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
        Assert.AreEqual("Preparing", doc.GetProperty("NewStatus").GetString());
        Assert.AreEqual(actorId.ToString(), doc.GetProperty("ActorId").GetString());
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
        Assert.IsTrue(doc.GetProperty("RefundRequired").GetBoolean());
        Assert.AreEqual("order_cancelled", doc.GetProperty("EventName").GetString());
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
        Assert.AreEqual("order_failed", doc.GetProperty("EventName").GetString());
        Assert.AreEqual("payment_timeout", doc.GetProperty("Reason").GetString());
    }
}
