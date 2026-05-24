using System.Text.Json;
using Gruuber.Rides.Application.Commands;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Gruuber.SharedKernel.Pricing;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Gruuber.Rides.Application;

internal sealed class RideRequestCoordinator
{
    private readonly RidesDbContext _db;
    private readonly ISurgePricingService _surge;
    private readonly IConnectionMultiplexer _redis;
    private readonly RideOutboxFactory _outboxFactory;
    private readonly ILogger _logger;

    public RideRequestCoordinator(RidesDbContext db, ISurgePricingService surge,
        IConnectionMultiplexer redis, RideOutboxFactory outboxFactory, ILogger logger)
    {
        _db = db;
        _surge = surge;
        _redis = redis;
        _outboxFactory = outboxFactory;
        _logger = logger;
    }

    public async Task<ApplicationResult<Commands.RequestRideResponse>> HandleAsync(
        Commands.RequestRideCommand command, CancellationToken cancellationToken = default)
    {
        if (command.RideType == "pool")
            return await HandlePoolAsync(command, cancellationToken);

        return await HandleSoloAsync(command, cancellationToken);
    }

    private async Task<ApplicationResult<Commands.RequestRideResponse>> HandleSoloAsync(
        Commands.RequestRideCommand command, CancellationToken cancellationToken)
    {
        var baseFare = 10.00m;
        var surgeResult = await _surge.ResolveAsync(command.RegionId, "ride", baseFare, cancellationToken);

        var ride = Ride.Create(
            command.RiderId, command.RideType, command.RegionId,
            command.PickupLat, command.PickupLng,
            command.DestLat, command.DestLng,
            surgeResult.BaseFare, surgeResult.Multiplier, surgeResult.FinalFare, surgeResult.Reason);

        var outboxEntry = _outboxFactory.CreateRideRequested(command.RegionId, ride.Id, ride.RiderId,
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

        return ApplicationResult<Commands.RequestRideResponse>.Accepted(
            new Commands.RequestRideResponse(ride.Id, ride.Status.ToString(), "pending_match", fareResponse));
    }

    private async Task<ApplicationResult<Commands.RequestRideResponse>> HandlePoolAsync(
        Commands.RequestRideCommand command, CancellationToken cancellationToken)
    {
        if (command.DestLat is null || command.DestLng is null)
            return ApplicationResult<Commands.RequestRideResponse>.Failure(
                "DEST_REQUIRED", "Pool rides require destination coordinates.", 400);

        var rate = await _db.PoolRegionRates
            .FirstOrDefaultAsync(r => r.RegionId == command.RegionId, cancellationToken);

        if (rate is null)
            return ApplicationResult<Commands.RequestRideResponse>.Failure(
                "POOL_NOT_AVAILABLE", "Pool rides are not available in this region.", 400);

        var baseFare = 10.00m;
        var discountedBase = baseFare * (1 - rate.DiscountPct);
        var surgeResult = await _surge.ResolveAsync(command.RegionId, "ride", discountedBase, cancellationToken);

        var ride = Ride.CreatePool(command.RiderId, command.RegionId,
            command.PickupLat, command.PickupLng,
            command.DestLat.Value, command.DestLng.Value,
            surgeResult.BaseFare, surgeResult.Multiplier, surgeResult.FinalFare);

        var outboxEntry = _outboxFactory.CreateRidePoolQueued(command.RegionId, ride.Id, ride.RiderId,
            command.PickupLat, command.PickupLng, command.DestLat, command.DestLng);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.Rides.Add(ride);
        _db.Set<RideOutboxEntry>().Add(outboxEntry);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

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

        var queueKey = $"pool_queue:{command.RegionId}";
        var score = (double)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var redisDb = _redis.GetDatabase();
        await redisDb.SortedSetAddAsync(queueKey, queueEntry, score);
        await redisDb.KeyExpireAsync(queueKey, TimeSpan.FromSeconds(rate.MatchTimeoutSecs));

        _logger.LogInformation(
            "Ride {RideId} created (pool) rider={RiderId} region={RegionId} timeout={Timeout}s",
            ride.Id, ride.RiderId, ride.RegionId, rate.MatchTimeoutSecs);

        return ApplicationResult<Commands.RequestRideResponse>.Accepted(
            new Commands.RequestRideResponse(ride.Id, ride.Status.ToString(), "pool_queued",
                null, rate.MatchTimeoutSecs, surgeResult.FinalFare));
    }
}