using System.Net;
using System.Security.Claims;
using StackExchange.Redis;

namespace Gruuber.Api.Infrastructure;

public class RedisRateLimiterMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisRateLimiterMiddleware> _logger;
    private readonly IConfiguration _configuration;

    // Token bucket Lua script: atomic check-and-decrement
    // KEYS[1] = rate_limit key
    // ARGV[1] = max tokens, ARGV[2] = refill interval seconds, ARGV[3] = current unix timestamp
    private const string TokenBucketLua = @"
local key = KEYS[1]
local max_tokens = tonumber(ARGV[1])
local interval = tonumber(ARGV[2])
local now = tonumber(ARGV[3])

local data = redis.call('HMGET', key, 'tokens', 'last_refill')
local tokens = tonumber(data[1])
local last_refill = tonumber(data[2])

if tokens == nil then
    tokens = max_tokens - 1
    redis.call('HMSET', key, 'tokens', tokens, 'last_refill', now)
    redis.call('EXPIRE', key, interval * 2)
    return 1
end

-- Refill tokens based on time elapsed
local elapsed = now - last_refill
local refill = math.floor(elapsed / interval * max_tokens)
if refill > 0 then
    tokens = math.min(max_tokens, tokens + refill)
    last_refill = now
end

if tokens <= 0 then
    return 0
end

tokens = tokens - 1
redis.call('HMSET', key, 'tokens', tokens, 'last_refill', last_refill)
redis.call('EXPIRE', key, interval * 2)
return 1
";

    public RedisRateLimiterMiddleware(
        RequestDelegate next,
        IConnectionMultiplexer redis,
        ILogger<RedisRateLimiterMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _redis = redis;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var (maxTokens, intervalSeconds) = GetLimits(context);
        var bucketKey = GetBucketKey(context);

        var db = _redis.GetDatabase();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        try
        {
            var result = (int)await db.ScriptEvaluateAsync(
                TokenBucketLua,
                new RedisKey[] { bucketKey },
                new RedisValue[] { maxTokens, intervalSeconds, now });

            if (result == 0)
            {
                _logger.LogWarning("Rate limit exceeded for key {BucketKey}", bucketKey);
                context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                context.Response.Headers["Retry-After"] = intervalSeconds.ToString();
                await context.Response.WriteAsJsonAsync(new
                {
                    ErrorCode = "RATE_LIMIT_EXCEEDED",
                    ErrorMessage = "Too many requests. Please slow down."
                });
                return;
            }
        }
        catch (Exception ex)
        {
            // Rate limiter failure should not block requests — fail open
            _logger.LogError(ex, "Rate limiter Redis error for key {BucketKey} — allowing request", bucketKey);
        }

        await _next(context);
    }

    private (int maxTokens, int intervalSeconds) GetLimits(HttpContext context)
    {
        var role = context.User?.FindFirstValue(ClaimTypes.Role);

        // Location update endpoint gets a tighter per-second limit for drivers
        if (context.Request.Path.StartsWithSegments("/v1/drivers/location") && role == "driver")
        {
            var perSecond = _configuration.GetValue<int>("RateLimiting:Driver:LocationUpdatesPerSecond", 20);
            return (perSecond, 1);
        }

        return role switch
        {
            "rider"      => (_configuration.GetValue<int>("RateLimiting:Rider:RequestsPerMinute", 100), 60),
            "driver"     => (_configuration.GetValue<int>("RateLimiting:Driver:RequestsPerMinute", 300), 60),
            "restaurant" => (_configuration.GetValue<int>("RateLimiting:Restaurant:RequestsPerMinute", 200), 60),
            _            => (_configuration.GetValue<int>("RateLimiting:Anonymous:RequestsPerMinute", 20), 60)
        };
    }

    private string GetBucketKey(HttpContext context)
    {
        var role = context.User?.FindFirstValue(ClaimTypes.Role) ?? "anonymous";
        var userId = context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User?.FindFirstValue("sub")
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

        // For location updates, use per-second bucket with a distinct key suffix
        if (context.Request.Path.StartsWithSegments("/v1/drivers/location") && role == "driver")
            return $"rate_limit:driver_location:{userId}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        return $"rate_limit:{role}:{userId}";
    }
}
