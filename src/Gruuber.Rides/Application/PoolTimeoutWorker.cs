using Gruuber.Rides.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gruuber.Rides.Application;

public class PoolTimeoutWorker : BackgroundService
{
    private readonly PoolTimeoutCoordinator _coordinator;
    private readonly TimeSpan _sweepInterval = TimeSpan.FromSeconds(30);

    public PoolTimeoutWorker(IServiceScopeFactory scopeFactory, ILogger<PoolTimeoutWorker> logger)
    {
        _coordinator = new PoolTimeoutCoordinator(scopeFactory, new RideOutboxFactory(), logger);
    }

    internal PoolTimeoutWorker(RidesDbContext db, ILogger<PoolTimeoutWorker> logger)
    {
        _coordinator = new PoolTimeoutCoordinator(db, new RideOutboxFactory(), logger);
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

    internal Task SweepAsync(CancellationToken ct) => _coordinator.SweepAsync(ct);
}