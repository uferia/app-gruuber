using Gruuber.Analytics.Application;
using Gruuber.Analytics.Domain;
using Gruuber.Analytics.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gruuber.Tests.Unit.Analytics;

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

    [Fact]
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
        Assert.Equal(1, driverStat.TripsCompleted);
        Assert.Equal(12.50m, driverStat.GrossEarnings);

        var adminStat = await db.AdminStatsDaily.SingleAsync(x => x.RegionId == 1);
        Assert.Equal(1, adminStat.TotalRides);
    }

    [Fact]
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
        Assert.Equal(5, stat.TripsCompleted);
        Assert.Equal(50.00m, stat.GrossEarnings);
    }

    [Fact]
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
        Assert.Equal(1, stat.TripsCompleted); // not 2
    }

    [Fact]
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
        Assert.Equal(1, restStat.OrdersCompleted);
        Assert.Equal(25.00m, restStat.GrossRevenue);

        var menuStats = await db.MenuItemStatsDaily
            .Where(x => x.RestaurantId == restaurantId).ToListAsync();
        Assert.Equal(2, menuStats.Count);
        Assert.Equal(2, menuStats.First(x => x.ItemName == "Burger").UnitsSold);
    }
}
