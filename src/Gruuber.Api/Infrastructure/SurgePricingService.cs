using System.Text.Json;
using Gruuber.Orders.Infrastructure;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Gruuber.SharedKernel.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Gruuber.Api.Infrastructure;

public class SurgePricingService : ISurgePricingService
{
    private readonly RidesDbContext _ridesDb;
    private readonly OrdersDbContext? _ordersDb;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<SurgePricingService> _logger;
    private const int CacheTtlSeconds = 60;

    public SurgePricingService(
        RidesDbContext ridesDb,
        OrdersDbContext? ordersDb,
        IConnectionMultiplexer redis,
        ILogger<SurgePricingService> logger)
    {
        _ridesDb = ridesDb;
        _ordersDb = ordersDb;
        _redis = redis;
        _logger = logger;
    }

    public async Task<SurgeResolution> ResolveAsync(
        int regionId, string rideType, decimal baseFare,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await LoadConfigAsync(regionId, rideType, cancellationToken);

            // Check time rule first — takes precedence over demand ratio
            // Each time rule may specify a timezone; default to UTC
            var activeTimeRule = config.TimeRules.FirstOrDefault(r =>
            {
                if (!r.IsActive) return false;
                var tz = r.TimeZoneId != null
                    ? TimeZoneInfo.FindSystemTimeZoneById(r.TimeZoneId)
                    : TimeZoneInfo.Utc;
                var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
                var localTime = TimeOnly.FromDateTime(localNow);
                var localDay = (int)localNow.DayOfWeek;
                return (r.DayOfWeek == null || r.DayOfWeek == localDay)
                    && localTime >= r.StartTime && localTime <= r.EndTime;
            });

            if (activeTimeRule != null)
            {
                _logger.LogInformation(
                    "Surge time_rule applied: region={RegionId} type={RideType} multiplier={Mul}",
                    regionId, rideType, activeTimeRule.Multiplier);
                return Build(baseFare, activeTimeRule.Multiplier, "time_rule");
            }

            // Demand ratio
            var (activeRequests, availableDrivers) = await GetDemandRatioInputsAsync(
                regionId, rideType, cancellationToken);

            var ratio = activeRequests / (decimal)Math.Max(availableDrivers, 1);

            var matchingTier = config.Tiers
                .Where(t => ratio >= t.DemandRatioThreshold)
                .OrderByDescending(t => t.DemandRatioThreshold)
                .FirstOrDefault();

            if (matchingTier == null)
                return Build(baseFare, 1.0m, null);

            var multiplier = Math.Min(matchingTier.Multiplier, matchingTier.MaxMultiplier);

            _logger.LogInformation(
                "Surge demand applied: region={RegionId} type={RideType} ratio={Ratio:F2} multiplier={Mul}",
                regionId, rideType, ratio, multiplier);

            return Build(baseFare, multiplier, "demand");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SurgePricingService failed for region={RegionId}; defaulting to 1.0x", regionId);
            return Build(baseFare, 1.0m, null);
        }
    }

    private static SurgeResolution Build(decimal baseFare, decimal multiplier, string? reason) =>
        new(multiplier, reason, baseFare, baseFare * multiplier);

    private async Task<(int activeRequests, long availableDrivers)> GetDemandRatioInputsAsync(
        int regionId, string rideType, CancellationToken ct)
    {
        int activeRequests;
        if (rideType == "food")
        {
            if (_ordersDb == null)
            {
                _logger.LogWarning("OrdersDb unavailable for food surge demand calculation; defaulting to 0");
                activeRequests = 0;
            }
            else
            {
                activeRequests = await _ordersDb.Orders
                    .CountAsync(o => o.RegionId == regionId && o.Status == Orders.Domain.OrderStatus.Placed, ct);
            }
        }
        else
        {
            activeRequests = await _ridesDb.Rides
                .CountAsync(r => r.RegionId == regionId && r.Status == RideStatus.Requested, ct);
        }

        long availableDrivers;
        try
        {
            var db = _redis.GetDatabase();
            var ttlKey = $"driver_ttl:{regionId}";
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            availableDrivers = await db.SortedSetLengthAsync(ttlKey, now - 1, double.PositiveInfinity);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            availableDrivers = 1;   // Redis unavailable fallback
        }

        return (activeRequests, availableDrivers);
    }

    private async Task<SurgeConfigBundle> LoadConfigAsync(
        int regionId, string rideType, CancellationToken ct)
    {
        var cacheKey = $"surge_config:{regionId}:{rideType}";
        try
        {
            var db = _redis.GetDatabase();
            var cached = await db.StringGetAsync(cacheKey);
            if (cached.HasValue)
                return JsonSerializer.Deserialize<SurgeConfigBundle>(cached!)
                    ?? new SurgeConfigBundle([], []);

            var bundle = await LoadFromDbAsync(regionId, rideType, ct);
            await db.StringSetAsync(cacheKey, JsonSerializer.Serialize(bundle),
                TimeSpan.FromSeconds(CacheTtlSeconds));
            return bundle;
        }
        catch (RedisException)
        {
            _logger.LogWarning("Redis unavailable for surge cache key={Key}, querying DB", cacheKey);
            return await LoadFromDbAsync(regionId, rideType, ct);
        }
    }

    private async Task<SurgeConfigBundle> LoadFromDbAsync(int regionId, string rideType, CancellationToken ct)
    {
        var tiers = await _ridesDb.SurgeConfigs
            .Where(s => s.RegionId == regionId && s.RideType == rideType)
            .ToListAsync(ct);
        var timeRules = await _ridesDb.SurgeTimeRules
            .Where(r => r.RegionId == regionId && r.RideType == rideType && r.IsActive)
            .ToListAsync(ct);
        return new SurgeConfigBundle(tiers, timeRules);
    }
}

internal record SurgeConfigBundle(
    List<SurgePricingConfig> Tiers,
    List<SurgeTimeRule> TimeRules);
