using Gruuber.Rides.Application;
using Gruuber.Rides.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Gruuber.Rides.Application.Commands;

public class MatchDriverHandler
{
    private readonly DriverMatchCoordinator _coordinator;

    public MatchDriverHandler(RidesDbContext db, IDriverScoringService scoring, ILogger<MatchDriverHandler> logger)
    {
        _coordinator = new DriverMatchCoordinator(db, scoring, new RideOutboxFactory(), logger);
    }

    public Task<ApplicationResult<MatchDriverResponse>> HandleAsync(
        MatchDriverCommand command,
        CancellationToken cancellationToken = default)
        => _coordinator.HandleAsync(command, cancellationToken);
}

public record MatchDriverResponse(Guid RideId, Guid? DriverId, string Status);
