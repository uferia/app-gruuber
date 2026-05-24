using Gruuber.Analytics.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Analytics.Application.Queries;

public class AdminDashboardQueryHandler
{
    private readonly AnalyticsDbContext _db;
    public AdminDashboardQueryHandler(AnalyticsDbContext db) => _db = db;

    public async Task<AdminSummaryResponse> GetSummaryAsync(int regionId, string period, CancellationToken ct)
    {
        var (fromDate, toDate) = GetDateRange(period);
        var rows = await _db.AdminStatsDaily
            .Where(x => x.RegionId == regionId && x.StatDate >= fromDate && x.StatDate <= toDate)
            .ToListAsync(ct);

        return new AdminSummaryResponse(
            TotalRides: rows.Sum(x => x.TotalRides),
            TotalPoolRides: rows.Sum(x => x.TotalPoolRides),
            TotalOrders: rows.Sum(x => x.TotalOrders),
            GrossPlatformRevenue: rows.Sum(x => x.GrossPlatformRevenue),
            ActiveDrivers: rows.Count > 0 ? (int)rows.Average(x => x.ActiveDrivers) : 0,
            ActiveRestaurants: rows.Count > 0 ? (int)rows.Average(x => x.ActiveRestaurants) : 0);
    }

    private static (DateOnly from, DateOnly to) GetDateRange(string period) =>
        period switch
        {
            "weekly" => (DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-6)), DateOnly.FromDateTime(DateTime.UtcNow)),
            "monthly" => (DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-29)), DateOnly.FromDateTime(DateTime.UtcNow)),
            _ => (DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow))
        };
}

public record AdminSummaryResponse(int TotalRides, int TotalPoolRides, int TotalOrders,
    decimal GrossPlatformRevenue, int ActiveDrivers, int ActiveRestaurants);
