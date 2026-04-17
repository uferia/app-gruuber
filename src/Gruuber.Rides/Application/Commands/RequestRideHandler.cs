using System.Text.Json;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gruuber.Rides.Application.Commands;

public class RequestRideHandler
{
    private readonly RidesDbContext _db;
    private readonly ILogger<RequestRideHandler> _logger;

    public RequestRideHandler(RidesDbContext db, ILogger<RequestRideHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ApplicationResult<RequestRideResponse>> HandleAsync(
        RequestRideCommand command,
        CancellationToken cancellationToken = default)
    {
        var ride = Ride.Create(command.RiderId, command.RideType, command.RegionId);

        var outboxEntry = new RideOutboxEntry
        {
            EventType = $"ride-events-{command.RegionId}",
            Payload = JsonSerializer.Serialize(new
            {
                EventName = "ride_requested",
                RideId = ride.Id,
                RiderId = ride.RiderId,
                command.PickupLat,
                command.PickupLng,
                RegionId = ride.RegionId,
                OccurredAt = DateTime.UtcNow
            })
        };

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.Rides.Add(ride);
        _db.Set<RideOutboxEntry>().Add(outboxEntry);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation("Ride {RideId} created for rider {RiderId} in region {RegionId}",
            ride.Id, ride.RiderId, ride.RegionId);

        return ApplicationResult<RequestRideResponse>.Accepted(
            new RequestRideResponse(ride.Id, ride.Status.ToString(), "pending_match"));
    }
}
