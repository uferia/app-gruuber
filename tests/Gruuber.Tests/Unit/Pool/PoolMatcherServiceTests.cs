using Gruuber.Rides.Application;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using Xunit;

public class PoolMatcherServiceTests
{
    private static RidesDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<RidesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new RidesDbContext(opts);
    }

    [Fact]
    public async Task TryMatchRidesAsync_MatchesTwoCompatibleRiders()
    {
        // Two riders going in compatible directions (detour < max)
        await using var db = CreateInMemoryDb();
        db.PoolRegionRates.Add(new PoolRegionRate { RegionId = 1, MaxDetourKm = 5.0m, MatchTimeoutSecs = 120 });

        var ride1 = Ride.CreatePool(Guid.NewGuid(), 1, 51.50, -0.10, 51.60, -0.05);
        var ride2 = Ride.CreatePool(Guid.NewGuid(), 1, 51.51, -0.11, 51.61, -0.06);
        db.Rides.AddRange(ride1, ride2);
        await db.SaveChangesAsync();

        var redis = new Mock<IConnectionMultiplexer>();
        var redisDbs = new Mock<IDatabase>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDbs.Object);

        var entry1 = $@"{{""RideId"":""{ride1.Id}"",""RiderId"":""{ride1.RiderId}"",""Lat"":51.50,""Lng"":-0.10,""DestLat"":51.60,""DestLng"":-0.05}}";
        var entry2 = $@"{{""RideId"":""{ride2.Id}"",""RiderId"":""{ride2.RiderId}"",""Lat"":51.51,""Lng"":-0.11,""DestLat"":51.61,""DestLng"":-0.06}}";

        var queueEntries = new SortedSetEntry[]
        {
            new(entry1, 1000),
            new(entry2, 1001)
        };
        redisDbs.Setup(r => r.SortedSetRangeByScoreWithScoresAsync(
            It.IsAny<RedisKey>(), It.IsAny<double>(), It.IsAny<double>(),
            It.IsAny<Exclude>(), It.IsAny<Order>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(queueEntries);

        // Lua script returns 2 (both removed successfully)
        redisDbs.Setup(r => r.ScriptEvaluateAsync(
            It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(2, ResultType.Integer));

        var matcher = new PoolMatcherService(db, redis.Object, NullLogger<PoolMatcherService>.Instance);
        var matched = await matcher.TryMatchRidesAsync(1, CancellationToken.None);

        Assert.True(matched);
        var updated1 = await db.Rides.FindAsync(ride1.Id);
        var updated2 = await db.Rides.FindAsync(ride2.Id);
        Assert.Equal(RideStatus.PoolMatched, updated1!.Status);
        Assert.Equal(RideStatus.PoolMatched, updated2!.Status);
        Assert.Equal(updated1.PoolTripId, updated2.PoolTripId);
        Assert.NotEqual(updated1.PoolSlot, updated2.PoolSlot);
    }

    [Fact]
    public async Task TryMatchRidesAsync_ReturnsNoMatch_WhenDetourExceedsMax()
    {
        await using var db = CreateInMemoryDb();
        db.PoolRegionRates.Add(new PoolRegionRate { RegionId = 1, MaxDetourKm = 1.0m, MatchTimeoutSecs = 120 });

        // Two riders very far apart — detour will exceed 1.0km
        var ride1 = Ride.CreatePool(Guid.NewGuid(), 1, 0.0, 0.0, 0.01, 0.01);
        var ride2 = Ride.CreatePool(Guid.NewGuid(), 1, 50.0, 50.0, 50.01, 50.01);
        db.Rides.AddRange(ride1, ride2);
        await db.SaveChangesAsync();

        var redis = new Mock<IConnectionMultiplexer>();
        var redisDbs = new Mock<IDatabase>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDbs.Object);

        var entry1 = $@"{{""RideId"":""{ride1.Id}"",""RiderId"":""{ride1.RiderId}"",""Lat"":0.0,""Lng"":0.0,""DestLat"":0.01,""DestLng"":0.01}}";
        var entry2 = $@"{{""RideId"":""{ride2.Id}"",""RiderId"":""{ride2.RiderId}"",""Lat"":50.0,""Lng"":50.0,""DestLat"":50.01,""DestLng"":50.01}}";

        redisDbs.Setup(r => r.SortedSetRangeByScoreWithScoresAsync(
            It.IsAny<RedisKey>(), It.IsAny<double>(), It.IsAny<double>(),
            It.IsAny<Exclude>(), It.IsAny<Order>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync([new SortedSetEntry(entry1, 1000), new SortedSetEntry(entry2, 1001)]);

        var matcher = new PoolMatcherService(db, redis.Object, NullLogger<PoolMatcherService>.Instance);
        var matched = await matcher.TryMatchRidesAsync(1, CancellationToken.None);

        Assert.False(matched);
        // Rides remain PoolQueued — not modified
        var r1 = await db.Rides.FindAsync(ride1.Id);
        Assert.Equal(RideStatus.PoolQueued, r1!.Status);
    }
}
