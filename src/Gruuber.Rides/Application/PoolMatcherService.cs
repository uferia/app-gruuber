using System.Text.Json;
using Confluent.Kafka;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Gruuber.Rides.Application;

/// <summary>
/// Kafka consumer that listens for ride_pool_queued events and attempts to match
/// compatible pool riders within the same region.
/// </summary>
public class PoolMatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectionMultiplexer _redis;
    private readonly IConfiguration? _configuration;
    private readonly ILogger<PoolMatcherService> _logger;

    // Lua script: atomically removes two members from the sorted set.
    // Returns number of successfully removed members (2 = success, <2 = race lost).
    private const string RemovePairLua = @"
        local r1 = redis.call('ZREM', KEYS[1], ARGV[1])
        local r2 = redis.call('ZREM', KEYS[1], ARGV[2])
        return r1 + r2";

    // Production constructor
    public PoolMatcherService(IServiceScopeFactory scopeFactory, IConnectionMultiplexer redis,
        IConfiguration configuration, ILogger<PoolMatcherService> logger)
    {
        _scopeFactory = scopeFactory;
        _redis = redis;
        _configuration = configuration;
        _logger = logger;
    }

    // Constructor for unit testing (direct DbContext injection)
    internal PoolMatcherService(RidesDbContext db, IConnectionMultiplexer redis, ILogger<PoolMatcherService> logger)
    {
        _redis = redis;
        _logger = logger;
        _scopeFactory = new DirectScopeFactory(db);
        _configuration = null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_configuration is null) return; // unit test mode

        var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        var groupId = _configuration["Kafka:PoolMatcherGroupId"] ?? "gruuber-pool-matcher";
        var regions = _configuration.GetSection("Kafka:RideRegions").Get<int[]>() ?? [1];

        var topics = regions.Select(r => $"ride-events-{r}").ToList();
        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(topics);

        _logger.LogInformation("PoolMatcherService subscribed to: {Topics}", string.Join(", ", topics));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result = null;
                try
                {
                    result = consumer.Consume(stoppingToken);
                    if (result?.Message?.Value is null) continue;

                    using var doc = JsonDocument.Parse(result.Message.Value);
                    if (!doc.RootElement.TryGetProperty("EventName", out var en) ||
                        en.GetString() != "ride_pool_queued") { consumer.Commit(result); continue; }

                    var regionId = doc.RootElement.GetProperty("RegionId").GetInt32();
                    await TryMatchRidesAsync(regionId, stoppingToken);
                    consumer.Commit(result);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "PoolMatcherService error processing message");
                    if (result is not null) consumer.Commit(result); // move past poison pill after logging
                    try { await Task.Delay(1000, stoppingToken); } catch (OperationCanceledException) { break; }
                }
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    /// <summary>
    /// Attempts to match the oldest waiting ride with a compatible rider in the same region.
    /// Returns true if a match was made.
    /// Exposed as internal for unit testing.
    /// </summary>
    internal async Task<bool> TryMatchRidesAsync(int regionId, CancellationToken ct)
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

            // Atomically remove both from queue
            var luaResult = await redisDb.ScriptEvaluateAsync(RemovePairLua,
                new RedisKey[] { queueKey },
                new RedisValue[] { oldest.Element, candidate.Element });

            var removed = (int)luaResult;
            if (removed < 2)
            {
                _logger.LogWarning("PoolMatcherService: Lua atomic remove got {Removed}/2 for region {RegionId}", removed, regionId);
                return false;
            }

            await AssignPoolTripAsync(db, oldestData.RideId, candidateData.RideId, regionId, ct);
            return true;
        }

        return false;
    }

    private async Task AssignPoolTripAsync(RidesDbContext db, Guid rideId1, Guid rideId2,
        int regionId, CancellationToken ct)
    {
        var ride1 = await db.Rides.FindAsync([rideId1], ct);
        var ride2 = await db.Rides.FindAsync([rideId2], ct);

        if (ride1 is null || ride2 is null)
        {
            var missingId = ride1 is null ? rideId1 : rideId2;
            _logger.LogError("PoolMatcherService: ride {MissingRideId} not found after Lua remove — emitting match_failed outbox event. RegionId={RegionId}", missingId, regionId);
            
            // Emit a compensation event so consumers can reconcile
            var failedOutbox = new RideOutboxEntry
            {
                EventType = "ride_pool_match_failed",
                Payload = JsonSerializer.Serialize(new
                {
                    EventName = "ride_pool_match_failed",
                    RideId1 = rideId1,
                    RideId2 = rideId2,
                    MissingRideId = missingId,
                    RegionId = regionId,
                    Reason = "ride_not_found",
                    OccurredAt = DateTime.UtcNow
                })
            };
            await using var compensationTx = await db.Database.BeginTransactionAsync(ct);
            db.Set<RideOutboxEntry>().Add(failedOutbox);
            await db.SaveChangesAsync(ct);
            await compensationTx.CommitAsync(ct);
            return;
        }

        var poolTripId = Guid.NewGuid();
        var ok1 = ride1.TryAssignPool(poolTripId, slot: 1, ride1.Version);
        var ok2 = ride2.TryAssignPool(poolTripId, slot: 2, ride2.Version);

        if (!ok1 || !ok2)
        {
            _logger.LogError("PoolMatcherService: optimistic concurrency failed during pool assignment trip={TripId}", poolTripId);
            return;
        }

        var outbox1 = BuildPoolMatchedOutbox(regionId, poolTripId, rideId1, rideId2);
        var outbox2 = BuildPoolMatchedOutbox(regionId, poolTripId, rideId2, rideId1);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Set<RideOutboxEntry>().AddRange(outbox1, outbox2);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "Pool trip {TripId} matched: rides {R1} (slot 1) and {R2} (slot 2) region={RegionId}",
            poolTripId, rideId1, rideId2, regionId);
    }

    private static RideOutboxEntry BuildPoolMatchedOutbox(int regionId, Guid poolTripId, Guid thisRideId, Guid otherRideId) =>
        new()
        {
            EventType = "ride_pool_matched",
            Payload = JsonSerializer.Serialize(new
            {
                EventName = "ride_pool_matched",
                PoolTripId = poolTripId,
                RideId = thisRideId,
                OtherRideId = otherRideId,
                RegionId = regionId,
                OccurredAt = DateTime.UtcNow
            })
        };

    /// <summary>
    /// Haversine distance between two riders' pickup points as a proxy for detour.
    /// </summary>
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

/// <summary>Minimal IServiceScopeFactory adapter for unit testing with a direct DbContext.</summary>
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
