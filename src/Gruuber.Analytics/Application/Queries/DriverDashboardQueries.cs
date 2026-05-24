using Gruuber.Analytics.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Analytics.Application.Queries;

public class DriverDashboardQueryHandler
{
    private readonly AnalyticsDbContext _db;
    public DriverDashboardQueryHandler(AnalyticsDbContext db) => _db = db;

    public async Task<DriverSummaryResponse> GetSummaryAsync(Guid driverId, string period, CancellationToken ct)
    {
        var (fromDate, toDate) = GetDateRange(period);
        var rows = await _db.DriverStatsDaily
            .Where(x => x.DriverId == driverId && x.StatDate >= fromDate && x.StatDate <= toDate)
            .ToListAsync(ct);

        return new DriverSummaryResponse(
            TripsCompleted: rows.Sum(x => x.TripsCompleted),
            TripsCancelled: rows.Sum(x => x.TripsCancelled),
            PoolTrips: rows.Sum(x => x.PoolTrips),
            GrossEarnings: rows.Sum(x => x.GrossEarnings),
            BonusEarnings: rows.Sum(x => x.BonusEarnings),
            PayoutAmount: rows.Sum(x => x.PayoutAmount),
            AvgRating: rows.Count > 0 ? rows.Average(x => x.AvgRating) : 0m,
            AcceptanceRate: rows.Count > 0 ? rows.Average(x => x.AcceptanceRate) : 0m,
            OnlineMinutes: rows.Sum(x => x.OnlineMinutes));
    }

    public async Task<PagedResponse<DriverTripRow>> GetTripsAsync(Guid driverId,
        DateOnly fromDate, DateOnly toDate, int page, int limit, CancellationToken ct)
    {
        var query = _db.DriverStatsDaily
            .Where(x => x.DriverId == driverId && x.StatDate >= fromDate && x.StatDate <= toDate)
            .OrderByDescending(x => x.StatDate);

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * limit).Take(limit)
            .Select(x => new DriverTripRow(x.StatDate, x.TripsCompleted, x.GrossEarnings))
            .ToListAsync(ct);

        return new PagedResponse<DriverTripRow>(items, total, page, limit);
    }

    private static (DateOnly from, DateOnly to) GetDateRange(string period) =>
        period switch
        {
            "weekly" => (DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-6)), DateOnly.FromDateTime(DateTime.UtcNow)),
            "monthly" => (DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-29)), DateOnly.FromDateTime(DateTime.UtcNow)),
            _ => (DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow)) // daily
        };
}

public record DriverSummaryResponse(int TripsCompleted, int TripsCancelled, int PoolTrips,
    decimal GrossEarnings, decimal BonusEarnings, decimal PayoutAmount,
    decimal AvgRating, decimal AcceptanceRate, int OnlineMinutes);

public record DriverTripRow(DateOnly Date, int Trips, decimal Earnings);
public record PagedResponse<T>(List<T> Items, int Total, int Page, int Limit);
