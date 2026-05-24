using Gruuber.Rides.Application.Commands;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gruuber.Tests.Unit.Pool;

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

    [Fact]
    public async Task HandleAsync_TransitionsToRequested_AndEmitsOutboxEvent()
    {
        await using var db = CreateInMemoryDb();
        var ride = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        db.Rides.Add(ride);
        await db.SaveChangesAsync();

        var handler = new AcceptSoloUpgradeHandler(db, NullLogger<AcceptSoloUpgradeHandler>.Instance);
        var cmd = new AcceptSoloUpgradeCommand(ride.Id, ExpectedVersion: 1, ride.RiderId, RegionId: 1);

        var result = await handler.HandleAsync(cmd);

        Assert.True(result.IsSuccess);
        Assert.Equal(202, result.StatusCode);

        var updated = await db.Rides.FindAsync(ride.Id);
        Assert.Equal(RideStatus.Requested, updated!.Status);
        Assert.Equal("solo", updated.RideType);

        var outbox = await db.Set<RideOutboxEntry>().SingleAsync();
        Assert.Contains("ride_pool_upgraded", outbox.Payload);
    }

    [Fact]
    public async Task HandleAsync_Returns404_WhenRideNotFound()
    {
        await using var db = CreateInMemoryDb();
        var handler = new AcceptSoloUpgradeHandler(db, NullLogger<AcceptSoloUpgradeHandler>.Instance);
        var result = await handler.HandleAsync(new AcceptSoloUpgradeCommand(Guid.NewGuid(), 1, Guid.NewGuid(), 1));

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task HandleAsync_Returns409_OnVersionMismatch()
    {
        await using var db = CreateInMemoryDb();
        var ride = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        db.Rides.Add(ride);
        await db.SaveChangesAsync();

        var handler = new AcceptSoloUpgradeHandler(db, NullLogger<AcceptSoloUpgradeHandler>.Instance);
        var result = await handler.HandleAsync(
            new AcceptSoloUpgradeCommand(ride.Id, ExpectedVersion: 99, ride.RiderId, RegionId: 1));

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("RESOURCE_CONFLICTED", result.ErrorCode);
    }

    [Fact]
    public async Task HandleAsync_Returns403_WhenRiderDoesNotOwnRide()
    {
        await using var db = CreateInMemoryDb();
        var ride = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        db.Rides.Add(ride);
        await db.SaveChangesAsync();

        var handler = new AcceptSoloUpgradeHandler(db, NullLogger<AcceptSoloUpgradeHandler>.Instance);
        var result = await handler.HandleAsync(
            new AcceptSoloUpgradeCommand(ride.Id, ExpectedVersion: 1, RiderId: Guid.NewGuid(), RegionId: 1));

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
