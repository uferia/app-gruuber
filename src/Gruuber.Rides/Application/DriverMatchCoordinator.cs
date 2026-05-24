using Gruuber.Rides.Application.Commands;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gruuber.Rides.Application;

internal sealed class DriverMatchCoordinator
{
    private readonly RidesDbContext _db;
    private readonly Commands.IDriverScoringService _scoring;
    private readonly RideOutboxFactory _outboxFactory;
    private readonly ILogger _logger;

    private const double W1 = 0.5;
    private const double W2 = 0.3;
    private const double W3 = 0.2;

    public DriverMatchCoordinator(RidesDbContext db, Commands.IDriverScoringService scoring,
        RideOutboxFactory outboxFactory, ILogger logger)
    {
        _db = db;
        _scoring = scoring;
        _outboxFactory = outboxFactory;
        _logger = logger;
    }

    public async Task<ApplicationResult<Commands.MatchDriverResponse>> HandleAsync(
        Commands.MatchDriverCommand command, CancellationToken cancellationToken = default)
    {
        var ride = await _db.Rides.FindAsync(new object[] { command.RideId }, cancellationToken);

        if (ride is null)
            return ApplicationResult<Commands.MatchDriverResponse>.Failure("RIDE_NOT_FOUND", "Ride not found.", 404);

        var candidates = await _scoring.GetScoredCandidatesAsync(
            command.RideId, command.RegionId, W1, W2, W3,
            ride.PickupLat, ride.PickupLng, cancellationToken);

        if (!candidates.Any())
        {
            _logger.LogWarning("No driver candidates found for Ride {RideId} in region {RegionId}",
                command.RideId, command.RegionId);
            return ApplicationResult<Commands.MatchDriverResponse>.Accepted(
                new Commands.MatchDriverResponse(command.RideId, null, "pending_match"));
        }

        var best = candidates.OrderByDescending(c => c.Score).First();

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var matched = ride.TryMatch(best.DriverId, command.ExpectedVersion);
        if (!matched)
            return ApplicationResult<Commands.MatchDriverResponse>.Conflict(ride.Id, ride.Version);

        _db.Set<RideOutboxEntry>().Add(_outboxFactory.CreateDriverMatched(command.RegionId, ride.Id, best.DriverId, best.Score));

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation("Driver {DriverId} matched to Ride {RideId} (score: {Score})",
            best.DriverId, ride.Id, best.Score);

        return ApplicationResult<Commands.MatchDriverResponse>.Success(
            new Commands.MatchDriverResponse(ride.Id, best.DriverId, ride.Status.ToString()));
    }
}