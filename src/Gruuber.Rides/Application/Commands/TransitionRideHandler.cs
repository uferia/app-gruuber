using System.Text.Json;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Gruuber.Rides.Application.Commands;

public class TransitionRideHandler
{
    private readonly RidesDbContext _db;
    private readonly ILogger<TransitionRideHandler> _logger;

    private static readonly Dictionary<RideStatus, RideStatus[]> AllowedTransitions = new()
    {
        [RideStatus.Requested] = [RideStatus.Cancelled],
        [RideStatus.Matched]   = [RideStatus.EnRoute, RideStatus.Cancelled],
        [RideStatus.EnRoute]   = [RideStatus.Arrived],
        [RideStatus.Arrived]   = [RideStatus.Completed],
    };

    public TransitionRideHandler(RidesDbContext db, ILogger<TransitionRideHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ApplicationResult<TransitionRideResponse>> HandleAsync(
        TransitionRideCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<RideStatus>(command.NewStatus, ignoreCase: true, out var targetStatus))
            return ApplicationResult<TransitionRideResponse>.Failure(
                "INVALID_STATUS", $"Unknown ride status: {command.NewStatus}", 400);

        var ride = await _db.Rides.FindAsync(new object[] { command.RideId }, cancellationToken);
        if (ride is null)
            return ApplicationResult<TransitionRideResponse>.Failure("RIDE_NOT_FOUND", "Ride not found.", 404);

        if (!AllowedTransitions.TryGetValue(ride.Status, out var allowed) || !allowed.Contains(targetStatus))
            return ApplicationResult<TransitionRideResponse>.Failure(
                "INVALID_TRANSITION",
                $"Cannot transition from {ride.Status} to {targetStatus}.", 400);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var transitioned = ride.TryTransition(targetStatus, command.ExpectedVersion);
        if (!transitioned)
            return ApplicationResult<TransitionRideResponse>.Conflict(ride.Id, ride.Version);

        _db.Set<RideOutboxEntry>().Add(new RideOutboxEntry
        {
            EventType = $"ride-events-{command.RegionId}",
            Payload = JsonSerializer.Serialize(new
            {
                EventName = "ride_status_changed",
                RideId = ride.Id,
                NewStatus = targetStatus.ToString(),
                ActorId = command.ActorId,
                RegionId = command.RegionId,
                OccurredAt = DateTime.UtcNow
            })
        });

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation("Ride {RideId} transitioned to {Status} by actor {ActorId}",
            ride.Id, targetStatus, command.ActorId);

        return ApplicationResult<TransitionRideResponse>.Success(
            new TransitionRideResponse(ride.Id, targetStatus.ToString()));
    }
}
