using Gruuber.Analytics.Application;
using Gruuber.Analytics.Domain;
using Gruuber.Analytics.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gruuber.Tests.Unit.Analytics;

[TestClass]
public class AnalyticsConsumerServiceTests
{
    private static AnalyticsDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AnalyticsDbContext(opts);
    }

    [TestMethod]
    public async Task ProcessRideCompleted_UpsertDriverStatsAndAdminStats()
    {
        await using var db = CreateInMemoryDb();
        var processor = new AnalyticsEventProcessor(db, NullLogger<AnalyticsEventProcessor>.Instance);

        var driverId = Guid.NewGuid();
        var payload = $@"{{
            ""EventName"": ""ride_completed"",
            ""RideId"": ""{Guid.NewGuid()}"",
            ""DriverId"": ""{driverId}"",
            ""RegionId"": 1,
            ""Fare"": 12.50,
            ""IsPool"": false,
            ""OccurredAt"": ""{DateTime.UtcNow:O}""
        }}";

        await processor.ProcessAsync(payload, CancellationToken.None);

        var driverStat = await db.DriverStatsDaily
            .SingleAsync(x => x.DriverId == driverId);
        driverStat.TripsCompleted.Should().Be(1);
        driverStat.GrossEarnings.Should().Be(12.50m);

        var adminStat = await db.AdminStatsDaily.SingleAsync(x => x.RegionId == 1);
        adminStat.TotalRides.Should().Be(1);
    }

    [TestMethod]
    public async Task ProcessRideCompleted_AccumulatesMultipleEvents()
    {
        await using var db = CreateInMemoryDb();
        var processor = new AnalyticsEventProcessor(db, NullLogger<AnalyticsEventProcessor>.Instance);
        var driverId = Guid.NewGuid();
        var today = DateTime.UtcNow.ToString("O");

        for (int i = 0; i < 5; i++)
        {
            var payload = $@"{{
                ""EventName"": ""ride_completed"",
                ""RideId"": ""{Guid.NewGuid()}"",
                ""DriverId"": ""{driverId}"",
                ""RegionId"": 1,
                ""Fare"": 10.00,
                ""IsPool"": false,
                ""OccurredAt"": ""{today}""
            }}";
            await processor.ProcessAsync(payload, CancellationToken.None);
        }

        var stat = await db.DriverStatsDaily.SingleAsync(x => x.DriverId == driverId);
        stat.TripsCompleted.Should().Be(5);
        stat.GrossEarnings.Should().Be(50.00m);
    }

    [TestMethod]
    public async Task ProcessDuplicateEvent_SkipsSecondUpsert()
    {
        await using var db = CreateInMemoryDb();
        var processor = new AnalyticsEventProcessor(db, NullLogger<AnalyticsEventProcessor>.Instance);
        var driverId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        var payload = $@"{{
            ""EventName"": ""ride_completed"",
            ""EventId"": ""{eventId}"",
            ""RideId"": ""{Guid.NewGuid()}"",
            ""DriverId"": ""{driverId}"",
            ""RegionId"": 1,
            ""Fare"": 10.00,
            ""IsPool"": false,
            ""OccurredAt"": ""{DateTime.UtcNow:O}""
        }}";

        await processor.ProcessAsync(payload, CancellationToken.None);
        await processor.ProcessAsync(payload, CancellationToken.None); // duplicate

        var stat = await db.DriverStatsDaily.SingleAsync(x => x.DriverId == driverId);
        stat.TripsCompleted.Should().Be(1); // not 2
    }

    [TestMethod]
    public async Task ProcessOrderDelivered_UpsertRestaurantAndMenuItemStats()
    {
        await using var db = CreateInMemoryDb();
        var processor = new AnalyticsEventProcessor(db, NullLogger<AnalyticsEventProcessor>.Instance);
        var restaurantId = Guid.NewGuid();

        var payload = $@"{{
            ""EventName"": ""order_delivered"",
            ""OrderId"": ""{Guid.NewGuid()}"",
            ""RestaurantId"": ""{restaurantId}"",
            ""RegionId"": 1,
            ""Revenue"": 25.00,
            ""PrepTimeSecs"": 600,
            ""Items"": [
                {{ ""ItemName"": ""Burger"", ""Quantity"": 2, ""Revenue"": 16.00 }},
                {{ ""ItemName"": ""Fries"", ""Quantity"": 1, ""Revenue"": 9.00 }}
            ],
            ""OccurredAt"": ""{DateTime.UtcNow:O}""
        }}";

        await processor.ProcessAsync(payload, CancellationToken.None);

        var restStat = await db.RestaurantStatsDaily.SingleAsync(x => x.RestaurantId == restaurantId);
        restStat.OrdersCompleted.Should().Be(1);
        restStat.GrossRevenue.Should().Be(25.00m);

        var menuStats = await db.MenuItemStatsDaily
            .Where(x => x.RestaurantId == restaurantId).ToListAsync();
        menuStats.Count.Should().Be(2);
        menuStats.First(x => x.ItemName == "Burger").UnitsSold.Should().Be(2);
    }
}
