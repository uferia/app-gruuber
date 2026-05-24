using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Gruuber.SharedKernel.Pricing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Gruuber.Api.Controllers;

[ApiController]
[Route("v1/surge")]
public class SurgeController : ControllerBase
{
    private readonly ISurgePricingService _surge;
    private readonly RidesDbContext _db;
    private readonly IConnectionMultiplexer _redis;

    public SurgeController(
        ISurgePricingService surge,
        RidesDbContext db,
        IConnectionMultiplexer redis)
    {
        _surge = surge;
        _db = db;
        _redis = redis;
    }

    /// <summary>GET /v1/surge/estimate — pre-booking surge preview (valid for ~30s)</summary>
    [HttpGet("estimate")]
    [Authorize]
    public async Task<IActionResult> GetEstimate(
        [FromQuery] int region_id,
        [FromQuery] string ride_type,
        CancellationToken cancellationToken)
    {
        if (ride_type is not ("ride" or "food"))
            return BadRequest(new { error = "ride_type must be 'ride' or 'food'" });

        var resolution = await _surge.ResolveAsync(region_id, ride_type, 1.0m, cancellationToken);
        return Ok(new
        {
            surge_multiplier = resolution.Multiplier,
            surge_reason = resolution.Reason,
            valid_for_secs = 30
        });
    }

    /// <summary>PUT /v1/admin/surge/config — update surge tiers and immediately invalidate Redis cache</summary>
    [HttpPut("/v1/admin/surge/config")]
    [Authorize(Policy = "admin")]
    public async Task<IActionResult> UpdateConfig(
        [FromBody] UpdateSurgeConfigRequest request,
        CancellationToken cancellationToken)
    {
        if (request.RideType is not ("ride" or "food"))
            return BadRequest(new { error = "ride_type must be 'ride' or 'food'" });
        if (request.MaxMultiplier <= 1.0m)
            return BadRequest(new { error = "max_multiplier must be > 1.0" });
        if (request.Tiers.Any(t => t.Multiplier <= 0 || t.DemandRatioThreshold < 0))
            return BadRequest(new { error = "tier values must be positive" });

        var existing = await _db.SurgeConfigs
            .Where(c => c.RegionId == request.RegionId && c.RideType == request.RideType)
            .ToListAsync(cancellationToken);
        _db.SurgeConfigs.RemoveRange(existing);

        foreach (var tier in request.Tiers)
        {
            _db.SurgeConfigs.Add(new SurgePricingConfig
            {
                RegionId = request.RegionId,
                RideType = request.RideType,
                DemandRatioThreshold = tier.DemandRatioThreshold,
                Multiplier = tier.Multiplier,
                MaxMultiplier = request.MaxMultiplier,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Invalidate Redis cache
        try
        {
            var cacheKey = $"surge_config:{request.RegionId}:{request.RideType}";
            await _redis.GetDatabase().KeyDeleteAsync(cacheKey);
        }
        catch { /* non-fatal */ }

        return Ok(new { updated = true });
    }
}

public record UpdateSurgeConfigRequest(
    int RegionId,
    string RideType,
    decimal MaxMultiplier,
    List<SurgeTierRequest> Tiers);

public record SurgeTierRequest(decimal DemandRatioThreshold, decimal Multiplier);
