using System.Text.Json;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gruuber.Rides.Application;

public class PoolTimeoutWorker : BackgroundService
{
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly RidesDbContext? _directDb;
    private readonly ILogger<PoolTimeoutWorker> _logger;
    private readonly TimeSpan _sweepInterval = TimeSpan.FromSeconds(30);

    // Production constructor
    public PoolTimeoutWorker(IServiceScopeFactory scopeFactory, ILogger<PoolTimeoutWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // Unit test constructor
    internal PoolTimeoutWorker(RidesDbContext db, ILogger<PoolTimeoutWorker> logger)
    {
        _directDb = db;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_sweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            await SweepAsync(stoppingToken);
        }
    }

    internal async Task SweepAsync(CancellationToken ct)
    {
        RidesDbContext db;
        IServiceScope? scope = null;

        if (_directDb is not null)
        {
            db = _directDb;
        }
        else
        {
            scope = _scopeFactory!.CreateScope();
            db = scope.ServiceProvider.GetRequiredService<RidesDbContext>();
        }

        try
        {
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

                // Transition expired rides to Cancelled
                foreach (var r in expiredRides)
                    r.CancelExpiredPool();

                var outboxEntries = expiredRides.Select(r => new RideOutboxEntry
                {
                    EventType = "ride_pool_timeout",
                    Payload = JsonSerializer.Serialize(new
                    {
                        EventName = "ride_pool_timeout",
                        RideId = r.Id,
                        RiderId = r.RiderId,
                        RegionId = rate.RegionId,
                        Reason = "no_match",
                        NotifyUser = true,
                        OccurredAt = now
                    })
                }).ToList();

                await using var tx = await db.Database.BeginTransactionAsync(ct);
                db.Set<RideOutboxEntry>().AddRange(outboxEntries);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                _logger.LogWarning(
                    "PoolTimeoutWorker: {Count} pool rides timed out in region {RegionId}",
                    expiredRides.Count, rate.RegionId);
            }
        }
        finally
        {
            scope?.Dispose();
        }
    }
}
