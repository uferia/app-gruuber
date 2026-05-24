using System.Text.Json;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Gruuber.SharedKernel.Pricing;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Gruuber.Rides.Application.Commands;

public class RequestRideHandler
{
    private readonly RidesDbContext _db;
    private readonly ISurgePricingService _surge;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RequestRideHandler> _logger;

    public RequestRideHandler(RidesDbContext db, ISurgePricingService surge,
        IConnectionMultiplexer redis, ILogger<RequestRideHandler> logger)
    {
        _db = db;
        _surge = surge;
        _redis = redis;
        _logger = logger;
    }

    public async Task<ApplicationResult<RequestRideResponse>> HandleAsync(
        RequestRideCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.RideType == "pool")
            return await HandlePoolAsync(command, cancellationToken);

        return await HandleSoloAsync(command, cancellationToken);
    }

    private async Task<ApplicationResult<RequestRideResponse>> HandleSoloAsync(
        RequestRideCommand command, CancellationToken cancellationToken)
    {
        var baseFare = 10.00m; // placeholder — real fare engine is out of scope for this PR
        var surgeResult = await _surge.ResolveAsync(command.RegionId, "ride", baseFare, cancellationToken);

        var ride = Ride.Create(
            command.RiderId, command.RideType, command.RegionId,
            command.PickupLat, command.PickupLng,
            command.DestLat, command.DestLng,
            surgeResult.BaseFare, surgeResult.Multiplier, surgeResult.FinalFare, surgeResult.Reason);

        var outboxEntry = BuildOutbox("ride_requested", command.RegionId, ride.Id, ride.RiderId,
            command.PickupLat, command.PickupLng, ride.SurgeMultiplier, ride.FinalFare);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.Rides.Add(ride);
        _db.Set<RideOutboxEntry>().Add(outboxEntry);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Ride {RideId} created for rider {RiderId} in region {RegionId} surge={SurgeMul}x",
            ride.Id, ride.RiderId, ride.RegionId, ride.SurgeMultiplier);

        FareEstimate? fareResponse = null;
        if (ride.BaseFare.HasValue)
        {
            fareResponse = new FareEstimate(
                ride.BaseFare.Value,
                ride.FinalFare!.Value,
                ride.SurgeMultiplier > 1.0m ? ride.SurgeMultiplier : null,
                ride.SurgeReason);
        }

        return ApplicationResult<RequestRideResponse>.Accepted(
            new RequestRideResponse(ride.Id, ride.Status.ToString(), "pending_match", fareResponse));
    }

    private async Task<ApplicationResult<RequestRideResponse>> HandlePoolAsync(
        RequestRideCommand command, CancellationToken cancellationToken)
    {
        if (command.DestLat is null || command.DestLng is null)
            return ApplicationResult<RequestRideResponse>.Failure(
                "DEST_REQUIRED", "Pool rides require destination coordinates.", 400);

        var rate = await _db.PoolRegionRates
            .FirstOrDefaultAsync(r => r.RegionId == command.RegionId, cancellationToken);

        if (rate is null)
            return ApplicationResult<RequestRideResponse>.Failure(
                "POOL_NOT_AVAILABLE", "Pool rides are not available in this region.", 400);

        var baseFare = 10.00m; // placeholder
        var discountedBase = baseFare * (1 - rate.DiscountPct);
        var surgeResult = await _surge.ResolveAsync(command.RegionId, "ride", discountedBase, cancellationToken);

        var ride = Ride.CreatePool(command.RiderId, command.RegionId,
            command.PickupLat, command.PickupLng,
            command.DestLat.Value, command.DestLng.Value,
            surgeResult.BaseFare, surgeResult.Multiplier, surgeResult.FinalFare);

        var queueEntry = JsonSerializer.Serialize(new
        {
            RideId = ride.Id,
            RiderId = ride.RiderId,
            Lat = command.PickupLat,
            Lng = command.PickupLng,
            DestLat = command.DestLat,
            DestLng = command.DestLng,
            RequestedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        var outboxEntry = new RideOutboxEntry
        {
            EventType = $"ride-events-{command.RegionId}",
            Payload = JsonSerializer.Serialize(new
            {
                EventName = "ride_pool_queued",
                RideId = ride.Id,
                RiderId = ride.RiderId,
                RegionId = ride.RegionId,
                Origin = new { Lat = command.PickupLat, Lng = command.PickupLng },
                Destination = new { Lat = command.DestLat, Lng = command.DestLng },
                RequestedAt = DateTime.UtcNow
            })
        };

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.Rides.Add(ride);
        _db.Set<RideOutboxEntry>().Add(outboxEntry);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        // Push to Redis pool queue (after DB tx commits)
        var queueKey = $"pool_queue:{command.RegionId}";
        var score = (double)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var redisDb = _redis.GetDatabase();
        await redisDb.SortedSetAddAsync(queueKey, queueEntry, score);
        await redisDb.KeyExpireAsync(queueKey, TimeSpan.FromSeconds(rate.MatchTimeoutSecs * 2));

        _logger.LogInformation(
            "Ride {RideId} created (pool) rider={RiderId} region={RegionId} timeout={Timeout}s",
            ride.Id, ride.RiderId, ride.RegionId, rate.MatchTimeoutSecs);

        return ApplicationResult<RequestRideResponse>.Accepted(
            new RequestRideResponse(ride.Id, ride.Status.ToString(), "pool_queued",
                null, rate.MatchTimeoutSecs, surgeResult.FinalFare));
    }

    private static RideOutboxEntry BuildOutbox(string eventName, int regionId, Guid rideId, Guid riderId,
        double pickupLat, double pickupLng, decimal surgeMul, decimal? finalFare) =>
        new()
        {
            EventType = $"ride-events-{regionId}",
            Payload = JsonSerializer.Serialize(new
            {
                EventName = eventName,
                RideId = rideId,
                RiderId = riderId,
                PickupLat = pickupLat,
                PickupLng = pickupLng,
                RegionId = regionId,
                SurgeMultiplier = surgeMul,
                FinalFare = finalFare,
                OccurredAt = DateTime.UtcNow
            })
        };
}

