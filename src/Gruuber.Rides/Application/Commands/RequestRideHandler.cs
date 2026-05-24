using System.Text.Json;
using Gruuber.Rides.Application;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Gruuber.SharedKernel.Pricing;
using Gruuber.SharedKernel.Results;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Gruuber.Rides.Application.Commands;

public class RequestRideHandler
{
    private readonly RideRequestCoordinator _coordinator;

    public RequestRideHandler(RidesDbContext db, ISurgePricingService surge,
        IConnectionMultiplexer redis, ILogger<RequestRideHandler> logger)
    {
        _coordinator = new RideRequestCoordinator(db, surge, redis, new RideOutboxFactory(), logger);
    }

    public Task<ApplicationResult<RequestRideResponse>> HandleAsync(
        RequestRideCommand command,
        CancellationToken cancellationToken = default)
        => _coordinator.HandleAsync(command, cancellationToken);
}

