using System.Text.Json;
using FluentAssertions;
using Gruuber.Orders.Application.Commands;
using Gruuber.Orders.Domain;
using Gruuber.Orders.Infrastructure;
using Gruuber.SharedKernel.Catalog;
using Gruuber.SharedKernel.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class TransitionOrderHandlerTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    private static OrdersDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new OrdersDbContext(opts);
    }

    private static TransitionOrderHandler Handler(OrdersDbContext db)
    {
        var catalog = new Mock<IRestaurantCatalogReader>();
        catalog.Setup(c => c.GetRestaurantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                new CatalogRestaurant(id, OwnerId, "Kanto Grill", true, true, 1, 14.5, 120.9));
        return new TransitionOrderHandler(db, catalog.Object, NullLogger<TransitionOrderHandler>.Instance);
    }

    private static async Task<Order> SeedOrder(OrdersDbContext db, PaymentMethod method = PaymentMethod.CardMock)
    {
        var order = Order.CreateForDelivery(Guid.NewGuid(), Guid.NewGuid(), 1, 14.60, 120.98, method);
        order.AddItem(Guid.NewGuid(), 1, 100m);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private static TransitionOrderCommand Cmd(
        Guid orderId, string newStatus, long version, Guid actor, string role,
        string? reason = null, string? note = null) =>
        new(orderId, newStatus, version, 1, actor, role, reason, note);

    [TestMethod]
    public async Task Accept_ByRestaurantOwner_Succeeds()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db);
        var handler = Handler(db);

        var result = await handler.HandleAsync(Cmd(order.Id, "Accepted", 1, OwnerId, "restaurant"));

        result.IsSuccess.Should().BeTrue();
        result.Data!.Status.Should().Be("Accepted");
    }

    [TestMethod]
    public async Task Accept_ByNonOwner_Returns403()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db);
        var handler = Handler(db);

        var result = await handler.HandleAsync(Cmd(order.Id, "Accepted", 1, Guid.NewGuid(), "restaurant"));

        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("FORBIDDEN");
    }

    [TestMethod]
    public async Task Accept_ByRider_Returns403()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db);
        var handler = Handler(db);

        var result = await handler.HandleAsync(Cmd(order.Id, "Accepted", 1, order.RiderId, "rider"));

        result.StatusCode.Should().Be(403);
    }

    [TestMethod]
    public async Task IllegalTransition_PlacedToReady_Returns400()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db);
        var handler = Handler(db);

        var result = await handler.HandleAsync(Cmd(order.Id, "Ready", 1, OwnerId, "restaurant"));

        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("INVALID_TRANSITION");
    }

    [TestMethod]
    public async Task StaleVersion_Returns409()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db);
        var handler = Handler(db);

        var result = await handler.HandleAsync(Cmd(order.Id, "Accepted", 99, OwnerId, "restaurant"));

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("RESOURCE_CONFLICTED");
    }

    [TestMethod]
    public async Task PickedUp_ByAssignedDriver_Succeeds()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db);
        var driverId = Guid.NewGuid();
        order.TryTransition(OrderStatus.Accepted, 1);   // v2
        order.TryTransition(OrderStatus.Preparing, 2);  // v3
        order.TryTransition(OrderStatus.Ready, 3);      // v4
        order.TryAssignDriver(driverId, 4);             // v5
        await db.SaveChangesAsync();
        var handler = Handler(db);

        var result = await handler.HandleAsync(Cmd(order.Id, "PickedUp", 5, driverId, "driver"));

        result.IsSuccess.Should().BeTrue();
        result.Data!.Status.Should().Be("PickedUp");
    }

    [TestMethod]
    public async Task PickedUp_ByUnassignedDriver_Returns403()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db);
        order.TryTransition(OrderStatus.Accepted, 1);
        order.TryTransition(OrderStatus.Preparing, 2);
        order.TryTransition(OrderStatus.Ready, 3);
        await db.SaveChangesAsync();
        var handler = Handler(db);

        var result = await handler.HandleAsync(Cmd(order.Id, "PickedUp", 4, Guid.NewGuid(), "driver"));

        result.StatusCode.Should().Be(403);
    }

    [TestMethod]
    public async Task Cancel_ByRiderWithCustomerReason_SucceedsAndEmitsRefundEvent()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db, PaymentMethod.CardMock);
        var handler = Handler(db);

        var result = await handler.HandleAsync(
            Cmd(order.Id, "Cancelled", 1, order.RiderId, "rider", "OrderedByMistake", "fat-fingered it"));

        result.IsSuccess.Should().BeTrue();
        var updated = await db.Orders.SingleAsync();
        updated.CancellationReason.Should().Be(OrderCancellationReason.OrderedByMistake);
        updated.CancelledByRole.Should().Be("rider");
        var events = await db.Set<OrderOutboxEntry>().ToListAsync();
        events.Should().HaveCount(2);
        var names = events.Select(e => JsonDocument.Parse(e.Payload).RootElement.GetProperty("EventName").GetString()).ToList();
        names.Should().BeEquivalentTo(new[] { "order_cancelled", "payment_refund_requested" });
    }

    [TestMethod]
    public async Task Cancel_CashOnDelivery_EmitsNoRefundEvent()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db, PaymentMethod.CashOnDelivery);
        var handler = Handler(db);

        await handler.HandleAsync(Cmd(order.Id, "Cancelled", 1, order.RiderId, "rider", "OrderedByMistake"));

        var events = await db.Set<OrderOutboxEntry>().ToListAsync();
        events.Should().HaveCount(1);
        JsonDocument.Parse(events[0].Payload).RootElement.GetProperty("EventName").GetString()
            .Should().Be("order_cancelled");
    }

    [TestMethod]
    public async Task Cancel_WithoutReason_Returns400()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db);
        var handler = Handler(db);

        var result = await handler.HandleAsync(Cmd(order.Id, "Cancelled", 1, order.RiderId, "rider"));

        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("REASON_REQUIRED");
    }

    [TestMethod]
    public async Task Cancel_RiderUsingRestaurantReason_Returns400()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db);
        var handler = Handler(db);

        var result = await handler.HandleAsync(Cmd(order.Id, "Cancelled", 1, order.RiderId, "rider", "TooBusy"));

        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("REASON_NOT_ALLOWED");
    }

    [TestMethod]
    public async Task Cancel_FromReady_SystemOnly()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db);
        order.TryTransition(OrderStatus.Accepted, 1);
        order.TryTransition(OrderStatus.Preparing, 2);
        order.TryTransition(OrderStatus.Ready, 3);
        await db.SaveChangesAsync();
        var handler = Handler(db);

        var riderAttempt = await handler.HandleAsync(
            Cmd(order.Id, "Cancelled", 4, order.RiderId, "rider", "TakingTooLong"));
        riderAttempt.StatusCode.Should().Be(403);

        var systemAttempt = await handler.HandleAsync(
            Cmd(order.Id, "Cancelled", 4, Guid.Empty, "system", "NoDriverAvailable"));
        systemAttempt.IsSuccess.Should().BeTrue();
    }

    [TestMethod]
    public async Task Delivered_EmitsOrderDeliveredWithRevenue()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db);
        var driverId = Guid.NewGuid();
        order.TryTransition(OrderStatus.Accepted, 1);
        order.TryTransition(OrderStatus.Preparing, 2);
        order.TryTransition(OrderStatus.Ready, 3);
        order.TryAssignDriver(driverId, 4);
        order.TryTransition(OrderStatus.PickedUp, 5);
        await db.SaveChangesAsync();
        var handler = Handler(db);

        var result = await handler.HandleAsync(Cmd(order.Id, "Delivered", 6, driverId, "driver"));

        result.IsSuccess.Should().BeTrue();
        var outbox = await db.Set<OrderOutboxEntry>().SingleAsync();
        using var doc = JsonDocument.Parse(outbox.Payload);
        doc.RootElement.GetProperty("EventName").GetString().Should().Be("order_delivered");
        doc.RootElement.GetProperty("RestaurantId").GetGuid().Should().Be(order.RestaurantId);
        doc.RootElement.GetProperty("Revenue").GetDecimal().Should().Be(100m);
    }
}
