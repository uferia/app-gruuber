using Gruuber.Analytics.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Analytics.Application.Queries;

public class RestaurantDashboardQueryHandler
{
    private readonly AnalyticsDbContext _db;
    public RestaurantDashboardQueryHandler(AnalyticsDbContext db) => _db = db;

    public async Task<RestaurantSummaryResponse> GetSummaryAsync(Guid restaurantId, string period, CancellationToken ct)
    {
        var (fromDate, toDate) = GetDateRange(period);
        var rows = await _db.RestaurantStatsDaily
            .Where(x => x.RestaurantId == restaurantId && x.StatDate >= fromDate && x.StatDate <= toDate)
            .ToListAsync(ct);

        var totalCompleted = rows.Sum(x => x.OrdersCompleted);
        var totalReceived = rows.Sum(x => x.OrdersReceived);
        var cancellationRate = totalReceived > 0
            ? (decimal)rows.Sum(x => x.OrdersCancelled) / totalReceived
            : 0m;

        return new RestaurantSummaryResponse(
            OrdersReceived: totalReceived,
            OrdersCompleted: totalCompleted,
            OrdersCancelled: rows.Sum(x => x.OrdersCancelled),
            GrossRevenue: rows.Sum(x => x.GrossRevenue),
            AvgPrepTimeSecs: rows.Count > 0 ? (int)rows.Average(x => x.AvgPrepTimeSecs) : 0,
            AvgRating: rows.Count > 0 ? rows.Average(x => x.AvgRating) : 0m,
            CancellationRate: cancellationRate);
    }

    public async Task<MenuPerformanceResponse> GetMenuPerformanceAsync(Guid restaurantId, string period, CancellationToken ct)
    {
        var (fromDate, toDate) = GetDateRange(period);
        var rows = await _db.MenuItemStatsDaily
            .Where(x => x.RestaurantId == restaurantId && x.StatDate >= fromDate && x.StatDate <= toDate)
            .ToListAsync(ct);

        var items = rows
            .GroupBy(x => x.ItemName)
            .Select(g => new MenuItemRow(g.Key, g.Sum(x => x.UnitsSold), g.Sum(x => x.Revenue)))
            .OrderByDescending(x => x.UnitsSold)
            .ToList();

        return new MenuPerformanceResponse(items);
    }

    private static (DateOnly from, DateOnly to) GetDateRange(string period) =>
        period switch
        {
            "weekly" => (DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-6)), DateOnly.FromDateTime(DateTime.UtcNow)),
            "monthly" => (DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-29)), DateOnly.FromDateTime(DateTime.UtcNow)),
            _ => (DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow))
        };
}

public record RestaurantSummaryResponse(int OrdersReceived, int OrdersCompleted, int OrdersCancelled,
    decimal GrossRevenue, int AvgPrepTimeSecs, decimal AvgRating, decimal CancellationRate);
public record MenuItemRow(string ItemName, int UnitsSold, decimal Revenue);
public record MenuPerformanceResponse(List<MenuItemRow> Items);
