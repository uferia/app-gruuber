using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gruuber.Rides.Application;

internal sealed class PoolTimeoutCoordinator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RideOutboxFactory _outboxFactory;
    private readonly ILogger _logger;

    public PoolTimeoutCoordinator(IServiceScopeFactory scopeFactory, RideOutboxFactory outboxFactory, ILogger logger)
    {
        _scopeFactory = scopeFactory;
        _outboxFactory = outboxFactory;
        _logger = logger;
    }

    internal PoolTimeoutCoordinator(RidesDbContext db, RideOutboxFactory outboxFactory, ILogger logger)
        : this(new DirectScopeFactory(db), outboxFactory, logger)
    {
    }

    public async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RidesDbContext>();

        var now = DateTime.UtcNow;
        var rates = await db.PoolRegionRates.ToListAsync(ct);

        foreach (var rate in rates)
        {
            var cutoff = now.AddSeconds(-rate.MatchTimeoutSecs);

            var expiredRides = await db.Rides
                .Where(r => r.Status == RideStatus.PoolQueued
                            && r.RegionId == rate.RegionId
                            && r.CreatedAt <= cutoff)
                .ToListAsync(ct);

            if (expiredRides.Count == 0) continue;

            foreach (var ride in expiredRides)
                ride.CancelExpiredPool();

            var outboxEntries = expiredRides.Select(ride => _outboxFactory.CreateRidePoolTimeout(rate.RegionId, ride)).ToList();

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            db.Set<RideOutboxEntry>().AddRange(outboxEntries);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogWarning(
                "PoolTimeoutWorker: {Count} pool rides timed out in region {RegionId}",
                expiredRides.Count, rate.RegionId);
        }
    }
}