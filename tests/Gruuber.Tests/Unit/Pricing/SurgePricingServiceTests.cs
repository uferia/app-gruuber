using Gruuber.Api.Infrastructure;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StackExchange.Redis;

[TestClass]
public class SurgePricingServiceTests
{
    private static RidesDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<RidesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RidesDbContext(opts);
    }

    [TestMethod]
    public async Task ResolveAsync_ReturnsMul1_WhenBelowAllThresholds()
    {
        await using var db = CreateInMemoryDb();
        db.SurgeConfigs.Add(new SurgePricingConfig
        { RegionId = 1, RideType = "ride", DemandRatioThreshold = 0.5m, Multiplier = 1.5m, MaxMultiplier = 3.0m });
        await db.SaveChangesAsync();

        var redis = new Mock<IConnectionMultiplexer>();
        var redisDbs = new Mock<IDatabase>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDbs.Object);
        // Cache miss → will query DB
        redisDbs.Setup(r => r.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        // 2 available drivers, 0 active rides → ratio = 0/2 = 0 → below 0.5 threshold
        redisDbs.Setup(r => r.SortedSetLengthAsync(It.IsAny<RedisKey>(), It.IsAny<double>(),
            It.IsAny<double>(), It.IsAny<Exclude>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(2);

        var svc = new SurgePricingService(db, null!, redis.Object, NullLogger<SurgePricingService>.Instance);
        var result = await svc.ResolveAsync(1, "ride", 10.00m);

        result.Multiplier.Should().Be(1.0m);
        result.Reason.Should().BeNull();
        result.FinalFare.Should().Be(10.00m);
    }

    [TestMethod]
    public async Task ResolveAsync_SelectsHighestMatchingTier()
    {
        await using var db = CreateInMemoryDb();
        db.SurgeConfigs.AddRange(
            new SurgePricingConfig { RegionId = 1, RideType = "ride", DemandRatioThreshold = 0.5m, Multiplier = 1.5m, MaxMultiplier = 3.0m },
            new SurgePricingConfig { RegionId = 1, RideType = "ride", DemandRatioThreshold = 0.75m, Multiplier = 2.0m, MaxMultiplier = 3.0m }
        );
        // Add 3 requested rides so ratio > 0.75 (3 rides / 1 driver = 3.0)
        db.Rides.AddRange(
            Ride.Create(Guid.NewGuid(), "ride", 1, 0, 0),
            Ride.Create(Guid.NewGuid(), "ride", 1, 0, 0),
            Ride.Create(Guid.NewGuid(), "ride", 1, 0, 0)
        );
        await db.SaveChangesAsync();

        var redis = new Mock<IConnectionMultiplexer>();
        var redisDbs = new Mock<IDatabase>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDbs.Object);
        redisDbs.Setup(r => r.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        redisDbs.Setup(r => r.SortedSetLengthAsync(It.IsAny<RedisKey>(), It.IsAny<double>(),
            It.IsAny<double>(), It.IsAny<Exclude>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);   // 1 driver → ratio = 3/1 = 3.0 → above 0.75

        var svc = new SurgePricingService(db, null!, redis.Object, NullLogger<SurgePricingService>.Instance);
        var result = await svc.ResolveAsync(1, "ride", 10.00m);

        result.Multiplier.Should().Be(2.0m);
        result.Reason.Should().Be("demand");
        result.FinalFare.Should().Be(20.00m);
    }

    [TestMethod]
    public async Task ResolveAsync_ClampsMul_ToMaxMultiplier()
    {
        await using var db = CreateInMemoryDb();
        db.SurgeConfigs.Add(new SurgePricingConfig
        { RegionId = 1, RideType = "ride", DemandRatioThreshold = 0.1m, Multiplier = 5.0m, MaxMultiplier = 3.0m });
        db.Rides.Add(Ride.Create(Guid.NewGuid(), "ride", 1, 0, 0));
        await db.SaveChangesAsync();

        var redis = new Mock<IConnectionMultiplexer>();
        var redisDbs = new Mock<IDatabase>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDbs.Object);
        redisDbs.Setup(r => r.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        redisDbs.Setup(r => r.SortedSetLengthAsync(It.IsAny<RedisKey>(), It.IsAny<double>(),
            It.IsAny<double>(), It.IsAny<Exclude>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(0);   // 0 drivers → ratio = 1/1 = 1.0 (max(0,1)=1) → above 0.1

        var svc = new SurgePricingService(db, null!, redis.Object, NullLogger<SurgePricingService>.Instance);
        var result = await svc.ResolveAsync(1, "ride", 10.00m);

        result.Multiplier.Should().Be(3.0m);   // clamped from 5.0
    }

    [TestMethod]
    public async Task ResolveAsync_UsesTimeRule_OverDemandRatio()
    {
        await using var db = CreateInMemoryDb();
        var now = TimeOnly.FromDateTime(DateTime.UtcNow);
        db.SurgeTimeRules.Add(new SurgeTimeRule
        {
            RegionId = 1, RideType = "ride",
            StartTime = now.AddMinutes(-30),
            EndTime = now.AddMinutes(30),
            Multiplier = 2.5m, IsActive = true
        });
        await db.SaveChangesAsync();

        var redis = new Mock<IConnectionMultiplexer>();
        var redisDbs = new Mock<IDatabase>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDbs.Object);
        redisDbs.Setup(r => r.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var svc = new SurgePricingService(db, null!, redis.Object, NullLogger<SurgePricingService>.Instance);
        var result = await svc.ResolveAsync(1, "ride", 8.00m);

        result.Multiplier.Should().Be(2.5m);
        result.Reason.Should().Be("time_rule");
    }

    [TestMethod]
    public async Task ResolveAsync_ReturnsMul1_WhenRedisThrows()
    {
        await using var db = CreateInMemoryDb();
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        var svc = new SurgePricingService(db, null!, redis.Object, NullLogger<SurgePricingService>.Instance);
        // Should not throw — fallback to DB which has no config → returns 1.0
        var result = await svc.ResolveAsync(1, "ride", 10.00m);

        result.Multiplier.Should().Be(1.0m);
    }
}
