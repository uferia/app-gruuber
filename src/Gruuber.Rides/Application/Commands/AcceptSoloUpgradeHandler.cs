using System.Text.Json;
using Gruuber.Rides.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Gruuber.Rides.Application.Commands;

public class AcceptSoloUpgradeHandler
{
    private readonly RidesDbContext _db;
    private readonly ILogger<AcceptSoloUpgradeHandler> _logger;

    public AcceptSoloUpgradeHandler(RidesDbContext db, ILogger<AcceptSoloUpgradeHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ApplicationResult<AcceptSoloUpgradeResponse>> HandleAsync(
        AcceptSoloUpgradeCommand command, CancellationToken cancellationToken = default)
    {
        var ride = await _db.Rides.FindAsync([command.RideId], cancellationToken);
        if (ride is null)
            return ApplicationResult<AcceptSoloUpgradeResponse>.Failure("NOT_FOUND", "Ride not found.", 404);

        if (ride.RiderId != command.RiderId)
            return ApplicationResult<AcceptSoloUpgradeResponse>.Failure("FORBIDDEN", "Not your ride.", 403);

        if (!ride.TryUpgradeToSolo(command.ExpectedVersion))
            return ApplicationResult<AcceptSoloUpgradeResponse>.Conflict(ride.Id, ride.Version);

        var outboxEntry = new RideOutboxEntry
        {
            EventType = "ride_pool_upgraded",
            Payload = JsonSerializer.Serialize(new
            {
                EventName = "ride_pool_upgraded",
                RideId = ride.Id,
                RiderId = ride.RiderId,
                RegionId = command.RegionId,
                PreviousStatus = "pool_queued",
                OccurredAt = DateTime.UtcNow
            })
        };

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.Set<RideOutboxEntry>().Add(outboxEntry);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation("Ride {RideId} upgraded to solo by rider {RiderId} region={RegionId}", 
            ride.Id, command.RiderId, command.RegionId);

        return ApplicationResult<AcceptSoloUpgradeResponse>.Accepted(
            new AcceptSoloUpgradeResponse(ride.Id, ride.Status.ToString()));
    }
}
