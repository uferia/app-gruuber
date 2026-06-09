using System.Collections.Concurrent;
using Gruuber.SharedKernel.Infrastructure;
using StackExchange.Redis;

namespace Gruuber.Api.Infrastructure.Redis;

/// <summary>
/// Multiton (concrete) — returns a region-scoped IDatabase from a shared IConnectionMultiplexer.
/// The same IDatabase instance is reused for each regionId (keyed singleton).
/// RegionId maps to a Redis logical database index (capped at 15 for Redis compatibility).
/// </summary>
public sealed class RegionedRedisDatabaseRegistry(IConnectionMultiplexer redis)
    : IRegionClientRegistry<IDatabase>
{
    private readonly ConcurrentDictionary<int, IDatabase> _clients = new();

    public IDatabase GetForRegion(int regionId)
    {
        return _clients.GetOrAdd(regionId, id =>
        {
            // Redis supports databases 0-15; larger region IDs share db 0 but key-prefix isolation applies
            var dbIndex = id <= 15 ? id : 0;
            return redis.GetDatabase(dbIndex);
        });
    }

    public IReadOnlyDictionary<int, IDatabase> All => _clients;
}
