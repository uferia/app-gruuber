using System.Text.Json;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Gruuber.Rides.Application;

internal sealed class PoolMatchCoordinator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectionMultiplexer _redis;
    private readonly RideOutboxFactory _outboxFactory;
    private readonly ILogger _logger;

    private const string RemovePairLua = @"
        local r1 = redis.call('ZREM', KEYS[1], ARGV[1])
        local r2 = redis.call('ZREM', KEYS[1], ARGV[2])
        return r1 + r2";

    public PoolMatchCoordinator(IServiceScopeFactory scopeFactory, IConnectionMultiplexer redis,
        RideOutboxFactory outboxFactory, ILogger logger)
    {
        _scopeFactory = scopeFactory;
        _redis = redis;
        _outboxFactory = outboxFactory;
        _logger = logger;
    }

    internal PoolMatchCoordinator(RidesDbContext db, IConnectionMultiplexer redis,
        RideOutboxFactory outboxFactory, ILogger logger)
        : this(new DirectScopeFactory(db), redis, outboxFactory, logger)
    {
    }

    public async Task<bool> TryMatchRidesAsync(int regionId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RidesDbContext>();

        var rate = await db.PoolRegionRates
            .FirstOrDefaultAsync(r => r.RegionId == regionId, ct);
        if (rate is null) return false;

        var queueKey = $"pool_queue:{regionId}";
        var redisDb = _redis.GetDatabase();

        var entries = await redisDb.SortedSetRangeByScoreWithScoresAsync(queueKey);
        if (entries.Length < 2) return false;

        var oldest = entries[0];
        var oldestData = JsonSerializer.Deserialize<PoolQueueEntry>(oldest.Element.ToString())!;

        for (int i = 1; i < entries.Length; i++)
        {
            var candidate = entries[i];
            var candidateData = JsonSerializer.Deserialize<PoolQueueEntry>(candidate.Element.ToString())!;

            var detourKm = CalculateDetourKm(oldestData, candidateData);
            if (detourKm > (double)rate.MaxDetourKm) continue;

            var luaResult = await redisDb.ScriptEvaluateAsync(RemovePairLua,
                new RedisKey[] { queueKey },
                new RedisValue[] { oldest.Element, candidate.Element });

            var removed = (int)luaResult;
            if (removed < 2)
            {
                _logger.LogWarning("PoolMatcherService: Lua atomic remove got {Removed}/2 for region {RegionId}", removed, regionId);
                return false;
            }

            return await AssignPoolTripAsync(db, oldestData.RideId, candidateData.RideId, regionId, ct);
        }

        return false;
    }

    private async Task<bool> AssignPoolTripAsync(RidesDbContext db, Guid rideId1, Guid rideId2,
        int regionId, CancellationToken ct)
    {
        var ride1 = await db.Rides.FindAsync([rideId1], ct);
        var ride2 = await db.Rides.FindAsync([rideId2], ct);

        if (ride1 is null || ride2 is null)
        {
            var missingId = ride1 is null ? rideId1 : rideId2;
            _logger.LogError("PoolMatcherService: ride {MissingRideId} not found after Lua remove — emitting match_failed outbox event. RegionId={RegionId}", missingId, regionId);

            var failedOutbox = _outboxFactory.CreateRidePoolMatchFailed(regionId, rideId1, rideId2, missingId);
            await using var compensationTx = await db.Database.BeginTransactionAsync(ct);
            db.Set<RideOutboxEntry>().Add(failedOutbox);
            await db.SaveChangesAsync(ct);
            await compensationTx.CommitAsync(ct);
            return false;
        }

        var poolTripId = Guid.NewGuid();
        var ok1 = ride1.TryAssignPool(poolTripId, slot: 1, ride1.Version);
        var ok2 = ride2.TryAssignPool(poolTripId, slot: 2, ride2.Version);

        if (!ok1 || !ok2)
        {
            _logger.LogError("PoolMatcherService: optimistic concurrency failed during pool assignment trip={TripId}", poolTripId);
            return false;
        }

        var outbox1 = _outboxFactory.CreateRidePoolMatched(regionId, poolTripId, rideId1, rideId2);
        var outbox2 = _outboxFactory.CreateRidePoolMatched(regionId, poolTripId, rideId2, rideId1);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Set<RideOutboxEntry>().AddRange(outbox1, outbox2);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "Pool trip {TripId} matched: rides {R1} (slot 1) and {R2} (slot 2) region={RegionId}",
            poolTripId, rideId1, rideId2, regionId);

        return true;
    }

    private static double CalculateDetourKm(PoolQueueEntry a, PoolQueueEntry b)
    {
        const double R = 6371.0;
        var dLat = ToRad(b.Lat - a.Lat);
        var dLng = ToRad(b.Lng - a.Lng);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(a.Lat)) * Math.Cos(ToRad(b.Lat)) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
}

internal record PoolQueueEntry(Guid RideId, Guid RiderId, double Lat, double Lng, double DestLat, double DestLng);

internal class DirectScopeFactory : IServiceScopeFactory
{
    private readonly RidesDbContext _db;
    public DirectScopeFactory(RidesDbContext db) => _db = db;
    public IServiceScope CreateScope() => new DirectScope(_db);

    private class DirectScope : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; }
        public DirectScope(RidesDbContext db) =>
            ServiceProvider = new DirectServiceProvider(db);
        public void Dispose() { }

        private class DirectServiceProvider : IServiceProvider
        {
            private readonly RidesDbContext _db;
            public DirectServiceProvider(RidesDbContext db) => _db = db;
            public object? GetService(Type t) => t == typeof(RidesDbContext) ? _db : null;
        }
    }
}