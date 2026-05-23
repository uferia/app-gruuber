using Gruuber.Tracking.Application;
using StackExchange.Redis;

namespace Gruuber.Tracking.Infrastructure;

public class RedisGeoService : IGeoService
{
    private readonly IConnectionMultiplexer _redis;
    private const int TtlSeconds = 10;

    public RedisGeoService(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task AddDriverLocationAsync(Guid driverId, double lat, double lng, int regionId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var geoKey = $"driver_locations:{regionId}";
        var ttlKey = $"driver_ttl:{regionId}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var batch = db.CreateBatch();
        var geoTask = batch.GeoAddAsync(geoKey, new GeoEntry(lng, lat, driverId.ToString()));
        var ttlTask = batch.SortedSetAddAsync(ttlKey, driverId.ToString(), now + TtlSeconds);
        batch.Execute();

        await Task.WhenAll(geoTask, ttlTask);
    }

    public async Task<IEnumerable<NearbyDriver>> GetNearbyDriversAsync(double lat, double lng, int regionId, double radiusKm = 5.0, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var geoKey = $"driver_locations:{regionId}";
        var ttlKey = $"driver_ttl:{regionId}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Prune stale members atomically via Lua (uses ZREM on both sorted sets)
        const string pruneScript = @"
            local stale = redis.call('ZRANGEBYSCORE', KEYS[2], '-inf', ARGV[1])
            for _, member in ipairs(stale) do
                redis.call('ZREM', KEYS[2], member)
                redis.call('ZREM', KEYS[1], member)
            end
            return #stale";

        await db.ScriptEvaluateAsync(pruneScript,
            new RedisKey[] { geoKey, ttlKey },
            new RedisValue[] { now - 1 });

        var results = await db.GeoRadiusAsync(
            geoKey, lng, lat, radiusKm,
            GeoUnit.Kilometers,
            order: Order.Ascending,
            options: GeoRadiusOptions.WithDistance);

        return results.Select(r => new NearbyDriver(Guid.Parse(r.Member!), r.Distance!.Value));
    }

    public async Task RemoveDriverAsync(Guid driverId, int regionId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var geoKey = $"driver_locations:{regionId}";
        var ttlKey = $"driver_ttl:{regionId}";

        var batch = db.CreateBatch();
        var geoTask = batch.GeoRemoveAsync(geoKey, driverId.ToString());
        var ttlTask = batch.SortedSetRemoveAsync(ttlKey, driverId.ToString());
        batch.Execute();

        await Task.WhenAll(geoTask, ttlTask);
    }
}
