using System.Text.Json;
using Gruuber.Rides.Application.Commands;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Gruuber.SharedKernel.Pricing;
using StackExchange.Redis;
using Xunit;

public class RequestPoolRideHandlerTests
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
    public async Task HandleAsync_PoolRide_ReturnsPoolQueuedStatus()
    {
        await using var db = CreateInMemoryDb();
        db.PoolRegionRates.Add(new PoolRegionRate
            { RegionId = 1, DiscountPct = 0.20m, MatchTimeoutSecs = 120, MaxDetourKm = 2.0m });
        await db.SaveChangesAsync();

        var redis = new Mock<IConnectionMultiplexer>();
        var redisDbs = new Mock<IDatabase>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDbs.Object);
        redisDbs.Setup(r => r.SortedSetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
            It.IsAny<double>(), It.IsAny<SortedSetWhen>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        redisDbs.Setup(r => r.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var surge = new Mock<ISurgePricingService>();
        surge.Setup(s => s.ResolveAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SurgeResolution(1.0m, null, 8.00m, 8.00m));

        var handler = new RequestRideHandler(db, surge.Object, redis.Object, NullLogger<RequestRideHandler>.Instance);
        var cmd = new RequestRideCommand(Guid.NewGuid(), "pool", 51.5, -0.1, 1, 51.6, -0.05);

        var result = await handler.HandleAsync(cmd);

        Assert.True(result.IsSuccess);
        Assert.Equal(202, result.StatusCode);
        Assert.Equal("PoolQueued", result.Data!.Status);

        var savedRide = await db.Rides.SingleAsync();
        Assert.Equal(RideStatus.PoolQueued, savedRide.Status);
    }

    [Fact]
    public async Task HandleAsync_PoolRide_ReturnsError_WhenDestMissing()
    {
        await using var db = CreateInMemoryDb();
        var redis = new Mock<IConnectionMultiplexer>();
        var surge = new Mock<ISurgePricingService>();

        var handler = new RequestRideHandler(db, surge.Object, redis.Object, NullLogger<RequestRideHandler>.Instance);
        // No DestLat/DestLng provided (null defaults)
        var cmd = new RequestRideCommand(Guid.NewGuid(), "pool", 51.5, -0.1, 1);

        var result = await handler.HandleAsync(cmd);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("DEST_REQUIRED", result.ErrorCode);
    }

    [Fact]
    public async Task HandleAsync_PoolRide_ReturnsError_WhenNoRegionRate()
    {
        await using var db = CreateInMemoryDb();
        // No PoolRegionRate seeded for region 1
        var redis = new Mock<IConnectionMultiplexer>();
        var surge = new Mock<ISurgePricingService>();
        surge.Setup(s => s.ResolveAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SurgeResolution(1.0m, null, 8.00m, 8.00m));

        var handler = new RequestRideHandler(db, surge.Object, redis.Object, NullLogger<RequestRideHandler>.Instance);
        var cmd = new RequestRideCommand(Guid.NewGuid(), "pool", 51.5, -0.1, 1, 51.6, -0.05);

        var result = await handler.HandleAsync(cmd);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("POOL_NOT_AVAILABLE", result.ErrorCode);
    }
}
