using System.Text.Json;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gruuber.Rides.Application.Commands;

public class MatchDriverHandler
{
    private readonly RidesDbContext _db;
    private readonly IDriverScoringService _scoring;
    private readonly ILogger<MatchDriverHandler> _logger;

    // Weights must sum to 1
    private const double W1 = 0.5; // proximity
    private const double W2 = 0.3; // rating
    private const double W3 = 0.2; // availability

    public MatchDriverHandler(RidesDbContext db, IDriverScoringService scoring, ILogger<MatchDriverHandler> logger)
    {
        _db = db;
        _scoring = scoring;
        _logger = logger;
    }

    public async Task<ApplicationResult<MatchDriverResponse>> HandleAsync(
        MatchDriverCommand command,
        CancellationToken cancellationToken = default)
    {
        var ride = await _db.Rides.FindAsync(new object[] { command.RideId }, cancellationToken);

        if (ride is null)
            return ApplicationResult<MatchDriverResponse>.Failure("RIDE_NOT_FOUND", "Ride not found.", 404);

        // Get candidates from scoring service (uses Redis GEO + DB ratings)
        var candidates = await _scoring.GetScoredCandidatesAsync(
            command.RideId, command.RegionId, W1, W2, W3, cancellationToken);

        if (!candidates.Any())
        {
            _logger.LogWarning("No driver candidates found for Ride {RideId} in region {RegionId}",
                command.RideId, command.RegionId);
            return ApplicationResult<MatchDriverResponse>.Accepted(
                new MatchDriverResponse(command.RideId, null, "pending_match"));
        }

        var best = candidates.OrderByDescending(c => c.Score).First();

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var matched = ride.TryMatch(best.DriverId, command.ExpectedVersion);
        if (!matched)
            return ApplicationResult<MatchDriverResponse>.Conflict(ride.Id, ride.Version);

        _db.Set<RideOutboxEntry>().Add(new RideOutboxEntry
        {
            EventType = $"ride-events-{command.RegionId}",
            Payload = JsonSerializer.Serialize(new
            {
                EventName = "driver_matched",
                RideId = ride.Id,
                DriverId = best.DriverId,
                Score = best.Score,
                RegionId = command.RegionId,
                OccurredAt = DateTime.UtcNow
            })
        });

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation("Driver {DriverId} matched to Ride {RideId} (score: {Score})",
            best.DriverId, ride.Id, best.Score);

        return ApplicationResult<MatchDriverResponse>.Success(
            new MatchDriverResponse(ride.Id, best.DriverId, ride.Status.ToString()));
    }
}

public record MatchDriverResponse(Guid RideId, Guid? DriverId, string Status);
