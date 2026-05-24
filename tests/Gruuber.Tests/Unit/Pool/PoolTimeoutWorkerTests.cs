using Gruuber.Rides.Application;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gruuber.Tests.Unit.Pool;

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

    [Fact]
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
        Assert.Single(outboxEvents);
        Assert.Contains("ride_pool_timeout", outboxEvents[0].Payload);
    }

    [Fact]
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
        Assert.Empty(outbox);
    }

    [Fact]
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
        Assert.Equal(1, firstSweepEvents);

        // Second sweep — ride is now Cancelled, should emit 0 additional events
        await worker.SweepAsync(CancellationToken.None);
        var secondSweepEvents = await db.Set<RideOutboxEntry>().CountAsync();
        Assert.Equal(1, secondSweepEvents); // still just 1 total
    }
}
