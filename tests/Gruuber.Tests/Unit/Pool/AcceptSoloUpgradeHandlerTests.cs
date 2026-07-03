using Gruuber.Rides.Application.Commands;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gruuber.Tests.Unit.Pool;

[TestClass]
public class AcceptSoloUpgradeHandlerTests
{
    private static RidesDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<RidesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new RidesDbContext(opts);
    }

    [TestMethod]
    public async Task HandleAsync_TransitionsToRequested_AndEmitsOutboxEvent()
    {
        await using var db = CreateInMemoryDb();
        var ride = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        db.Rides.Add(ride);
        await db.SaveChangesAsync();

        var handler = new AcceptSoloUpgradeHandler(db, NullLogger<AcceptSoloUpgradeHandler>.Instance);
        var cmd = new AcceptSoloUpgradeCommand(ride.Id, ExpectedVersion: 1, ride.RiderId, RegionId: 1);

        var result = await handler.HandleAsync(cmd);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(202);

        var updated = await db.Rides.FindAsync(ride.Id);
        updated!.Status.Should().Be(RideStatus.Requested);
        updated.RideType.Should().Be("solo");

        var outbox = await db.Set<RideOutboxEntry>().SingleAsync();
        outbox.Payload.Should().Contain("ride_pool_upgraded");
    }

    [TestMethod]
    public async Task HandleAsync_Returns404_WhenRideNotFound()
    {
        await using var db = CreateInMemoryDb();
        var handler = new AcceptSoloUpgradeHandler(db, NullLogger<AcceptSoloUpgradeHandler>.Instance);
        var result = await handler.HandleAsync(new AcceptSoloUpgradeCommand(Guid.NewGuid(), 1, Guid.NewGuid(), 1));

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [TestMethod]
    public async Task HandleAsync_Returns409_OnVersionMismatch()
    {
        await using var db = CreateInMemoryDb();
        var ride = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        db.Rides.Add(ride);
        await db.SaveChangesAsync();

        var handler = new AcceptSoloUpgradeHandler(db, NullLogger<AcceptSoloUpgradeHandler>.Instance);
        var result = await handler.HandleAsync(
            new AcceptSoloUpgradeCommand(ride.Id, ExpectedVersion: 99, ride.RiderId, RegionId: 1));

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("RESOURCE_CONFLICTED");
    }

    [TestMethod]
    public async Task HandleAsync_Returns403_WhenRiderDoesNotOwnRide()
    {
        await using var db = CreateInMemoryDb();
        var ride = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        db.Rides.Add(ride);
        await db.SaveChangesAsync();

        var handler = new AcceptSoloUpgradeHandler(db, NullLogger<AcceptSoloUpgradeHandler>.Instance);
        var result = await handler.HandleAsync(
            new AcceptSoloUpgradeCommand(ride.Id, ExpectedVersion: 1, RiderId: Guid.NewGuid(), RegionId: 1));

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
}
