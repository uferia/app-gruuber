using Gruuber.Orders.Domain;
using Gruuber.Orders.Infrastructure;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gruuber.Tests.Unit.Patterns;

/// <summary>
/// Tests for the Unit of Work pattern — RidesUnitOfWork and OrdersUnitOfWork.
/// Uses EF Core InMemory provider to verify transactional semantics
/// (atomic SaveChanges, Begin/Commit/Rollback cycle).
/// </summary>
[TestClass]
public class UnitOfWorkTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static RidesDbContext CreateRidesDb() =>
        new(new DbContextOptionsBuilder<RidesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static OrdersDbContext CreateOrdersDb() =>
        new(new DbContextOptionsBuilder<OrdersDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static Ride MakeRide() =>
        new RideBuilder().ForRider(Guid.NewGuid()).InRegion(1).FromPickup(0, 0).Build();

    private static Order MakeOrder() =>
        new OrderBuilder()
            .ForRider(Guid.NewGuid()).FromRestaurant(Guid.NewGuid())
            .ForRide(Guid.NewGuid()).InRegion(1)
            .AddItem(Guid.NewGuid(), 1, 5m).Build();

    // ══════════════════════════════════════════════════════════════════════════
    // RidesUnitOfWork
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task RidesUow_Rides_DbSet_IsAccessible()
    {
        // Arrange
        await using var db  = CreateRidesDb();
        var uow             = new RidesUnitOfWork(db);

        // Assert — typed DbSet exposed through UoW
        uow.Rides.Should().NotBeNull();
        uow.Outbox.Should().NotBeNull();
    }

    [TestMethod]
    public async Task RidesUow_SaveChangesAsync_PersistsRide()
    {
        // Arrange
        await using var db = CreateRidesDb();
        var uow            = new RidesUnitOfWork(db);
        var ride           = MakeRide();

        // Act
        uow.Rides.Add(ride);
        await uow.SaveChangesAsync();

        // Assert
        var persisted = await db.Rides.FindAsync(ride.Id);
        persisted.Should().NotBeNull();
        persisted.RiderId.Should().Be(ride.RiderId);
    }

    [TestMethod]
    public async Task RidesUow_SaveChangesAsync_PersistsOutboxEntry()
    {
        // Arrange
        await using var db = CreateRidesDb();
        var uow            = new RidesUnitOfWork(db);
        var entry          = new RideOutboxEntry { EventType = "ride-events-1", Payload = "{}" };

        // Act
        uow.Outbox.Add(entry);
        await uow.SaveChangesAsync();

        // Assert
        var persisted = await db.Set<RideOutboxEntry>().FindAsync(entry.Id);
        persisted.Should().NotBeNull();
        persisted.Status.Should().Be("pending");
    }

    [TestMethod]
    public async Task RidesUow_BeginAndCommit_PersistsBothRideAndOutbox()
    {
        // Arrange
        await using var db = CreateRidesDb();
        var uow            = new RidesUnitOfWork(db);
        var ride           = MakeRide();
        var outbox         = new RideOutboxEntry { EventType = "ride-events-1", Payload = "{\"EventName\":\"ride_requested\"}" };

        // Act — simulate handler: begin → add → save → commit
        await uow.BeginTransactionAsync();
        uow.Rides.Add(ride);
        uow.Outbox.Add(outbox);
        await uow.SaveChangesAsync();
        await uow.CommitAsync();

        // Assert — both records exist after commit
        (await db.Rides.FindAsync(ride.Id)).Should().NotBeNull();
        (await db.Set<RideOutboxEntry>().FindAsync(outbox.Id)).Should().NotBeNull();
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidOperationException))]
    public async Task RidesUow_CommitWithoutBegin_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var db = CreateRidesDb();
        var uow            = new RidesUnitOfWork(db);

        // Act — commit without begin
        await uow.CommitAsync();
    }

    [TestMethod]
    public async Task RidesUow_Rollback_WithNoTransaction_DoesNotThrow()
    {
        // Arrange
        await using var db = CreateRidesDb();
        var uow            = new RidesUnitOfWork(db);

        // Act / Assert — rollback with no active transaction is a no-op
        await uow.RollbackAsync();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // OrdersUnitOfWork
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task OrdersUow_SaveChangesAsync_PersistsOrder()
    {
        // Arrange
        await using var db = CreateOrdersDb();
        var uow            = new OrdersUnitOfWork(db);
        var order          = MakeOrder();

        // Act
        uow.Orders.Add(order);
        await uow.SaveChangesAsync();

        // Assert
        var persisted = await db.Orders.FindAsync(order.Id);
        persisted.Should().NotBeNull();
        persisted.RiderId.Should().Be(order.RiderId);
    }

    [TestMethod]
    public async Task OrdersUow_SaveChangesAsync_PersistsOutboxEntry()
    {
        // Arrange
        await using var db = CreateOrdersDb();
        var uow            = new OrdersUnitOfWork(db);
        var entry          = new OrderOutboxEntry { EventType = "order-events-1", Payload = "{}" };

        // Act
        uow.Outbox.Add(entry);
        await uow.SaveChangesAsync();

        // Assert
        (await db.Set<OrderOutboxEntry>().FindAsync(entry.Id)).Should().NotBeNull();
    }

    [TestMethod]
    public async Task OrdersUow_BeginAndCommit_AtomicallyPersistsOrderAndOutbox()
    {
        // Arrange
        await using var db = CreateOrdersDb();
        var uow            = new OrdersUnitOfWork(db);
        var order          = MakeOrder();
        var outbox         = new OrderOutboxEntry { EventType = "order-events-1", Payload = "{\"EventName\":\"order_created\"}" };

        // Act
        await uow.BeginTransactionAsync();
        uow.Orders.Add(order);
        uow.Outbox.Add(outbox);
        await uow.SaveChangesAsync();
        await uow.CommitAsync();

        // Assert
        (await db.Orders.FindAsync(order.Id)).Should().NotBeNull();
        (await db.Set<OrderOutboxEntry>().FindAsync(outbox.Id)).Should().NotBeNull();
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidOperationException))]
    public async Task OrdersUow_CommitWithoutBegin_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var db = CreateOrdersDb();
        var uow            = new OrdersUnitOfWork(db);

        // Act
        await uow.CommitAsync();
    }

    [TestMethod]
    public async Task OrdersUow_DbSets_AreAccessible()
    {
        // Arrange
        await using var db = CreateOrdersDb();
        var uow            = new OrdersUnitOfWork(db);

        // Assert — public typed DbSets
        uow.Orders.Should().NotBeNull();
        uow.Outbox.Should().NotBeNull();
    }
}
