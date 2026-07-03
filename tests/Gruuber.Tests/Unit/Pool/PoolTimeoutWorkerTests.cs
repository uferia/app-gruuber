using Gruuber.Rides.Application;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gruuber.Tests.Unit.Pool;

[TestClass]
public class PoolTimeoutWorkerTests
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
    public async Task SweepAsync_EmitsTimeoutOutboxEvent_ForExpiredPoolQueuedRides()
    {
        await using var db = CreateInMemoryDb();
        db.PoolRegionRates.Add(new PoolRegionRate { RegionId = 1, MatchTimeoutSecs = 0 });

        // Ride created with timeout=0 → already expired
        var expiredRide = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        db.Rides.Add(expiredRide);
        await db.SaveChangesAsync();

        var worker = new PoolTimeoutWorker(db, NullLogger<PoolTimeoutWorker>.Instance);
        await worker.SweepAsync(CancellationToken.None);

        var outboxEvents = await db.Set<RideOutboxEntry>().ToListAsync();
        outboxEvents.Should().ContainSingle();
        outboxEvents[0].Payload.Should().Contain("ride_pool_timeout");
    }

    [TestMethod]
    public async Task SweepAsync_DoesNotTouch_FreshPoolQueuedRides()
    {
        await using var db = CreateInMemoryDb();
        db.PoolRegionRates.Add(new PoolRegionRate { RegionId = 1, MatchTimeoutSecs = 120 });

        var freshRide = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        db.Rides.Add(freshRide);
        await db.SaveChangesAsync();

        var worker = new PoolTimeoutWorker(db, NullLogger<PoolTimeoutWorker>.Instance);
        await worker.SweepAsync(CancellationToken.None);

        var outbox = await db.Set<RideOutboxEntry>().ToListAsync();
        outbox.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SweepAsync_IsIdempotent_DoesNotDuplicateEvents_OnSecondSweep()
    {
        await using var db = CreateInMemoryDb();
        db.PoolRegionRates.Add(new PoolRegionRate { RegionId = 1, MatchTimeoutSecs = 0 });

        var expiredRide = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        db.Rides.Add(expiredRide);
        await db.SaveChangesAsync();

        var worker = new PoolTimeoutWorker(db, NullLogger<PoolTimeoutWorker>.Instance);

        // First sweep — should emit 1 event
        await worker.SweepAsync(CancellationToken.None);
        var firstSweepEvents = await db.Set<RideOutboxEntry>().CountAsync();
        firstSweepEvents.Should().Be(1);

        // Second sweep — ride is now Cancelled, should emit 0 additional events
        await worker.SweepAsync(CancellationToken.None);
        var secondSweepEvents = await db.Set<RideOutboxEntry>().CountAsync();
        secondSweepEvents.Should().Be(1); // still just 1 total
    }
}
