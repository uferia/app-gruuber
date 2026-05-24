using Gruuber.Analytics.Application.Queries;
using Gruuber.Analytics.Domain;
using Gruuber.Analytics.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Gruuber.Tests.Unit.Analytics;

public class DashboardQueryTests
{
    private static AnalyticsDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AnalyticsDbContext(opts);
    }

    [Fact]
    public async Task DriverSummary_WeeklyPeriod_SumsSevenDailyRows()
    {
        await using var db = CreateInMemoryDb();
        var driverId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        for (int i = 0; i < 7; i++)
        {
            db.DriverStatsDaily.Add(new DriverStatsDaily
            {
                DriverId = driverId, RegionId = 1,
                StatDate = today.AddDays(-i),
                TripsCompleted = 5, GrossEarnings = 50.00m
            });
        }
        await db.SaveChangesAsync();

        var handler = new DriverDashboardQueryHandler(db);
        var result = await handler.GetSummaryAsync(driverId, "weekly", CancellationToken.None);

        Assert.Equal(35, result.TripsCompleted);
        Assert.Equal(350.00m, result.GrossEarnings);
    }

    [Fact]
    public async Task DriverSummary_NoPeriodData_ReturnsZeroValuedSummary()
    {
        await using var db = CreateInMemoryDb();
        var handler = new DriverDashboardQueryHandler(db);
        var result = await handler.GetSummaryAsync(Guid.NewGuid(), "daily", CancellationToken.None);

        Assert.Equal(0, result.TripsCompleted);
        Assert.Equal(0m, result.GrossEarnings);
        // Must return 200 with zeros, not throw
    }

    [Fact]
    public async Task RestaurantMenuPerformance_SortedByUnitsSoldDesc()
    {
        await using var db = CreateInMemoryDb();
        var restaurantId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        db.MenuItemStatsDaily.AddRange(
            new MenuItemStatsDaily { RestaurantId = restaurantId, ItemName = "Burger", StatDate = today, UnitsSold = 10, Revenue = 80m },
            new MenuItemStatsDaily { RestaurantId = restaurantId, ItemName = "Fries", StatDate = today, UnitsSold = 25, Revenue = 50m }
        );
        await db.SaveChangesAsync();

        var handler = new RestaurantDashboardQueryHandler(db);
        var result = await handler.GetMenuPerformanceAsync(restaurantId, "daily", CancellationToken.None);

        Assert.Equal("Fries", result.Items[0].ItemName);  // highest units_sold first
        Assert.Equal("Burger", result.Items[1].ItemName);
    }
}
