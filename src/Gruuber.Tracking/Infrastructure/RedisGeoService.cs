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
        var key = $"driver_locations:{regionId}";

        await db.GeoAddAsync(key, new GeoEntry(lng, lat, driverId.ToString()));

        // Set TTL on the key so stale drivers are evicted
        await db.KeyExpireAsync(key, TimeSpan.FromSeconds(TtlSeconds));
    }

    public async Task<IEnumerable<NearbyDriver>> GetNearbyDriversAsync(double lat, double lng, int regionId, double radiusKm = 5.0, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = $"driver_locations:{regionId}";

        var results = await db.GeoRadiusAsync(
            key,
            lng, lat,
            radiusKm,
            GeoUnit.Kilometers,
            order: Order.Ascending,
            options: GeoRadiusOptions.WithDistance);

        return results.Select(r => new NearbyDriver(Guid.Parse(r.Member!), r.Distance!.Value));
    }

    public async Task RemoveDriverAsync(Guid driverId, int regionId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        await db.GeoRemoveAsync($"driver_locations:{regionId}", driverId.ToString());
    }
}
