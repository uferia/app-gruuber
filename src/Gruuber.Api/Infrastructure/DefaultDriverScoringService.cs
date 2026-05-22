using Gruuber.Rides.Application.Commands;
using Gruuber.Tracking.Application;

namespace Gruuber.Api.Infrastructure;

/// <summary>
/// Scores nearby drivers using Redis GEO for proximity.
/// Rating and availability default to 1.0 until a driver-profile service is available.
/// Score = w1*(1/(1+distKm)) + w2*rating + w3*availability
/// </summary>
public class DefaultDriverScoringService : IDriverScoringService
{
    private readonly IGeoService _geo;
    private readonly ILogger<DefaultDriverScoringService> _logger;

    public DefaultDriverScoringService(IGeoService geo, ILogger<DefaultDriverScoringService> logger)
    {
        _geo = geo;
        _logger = logger;
    }

    public async Task<IEnumerable<DriverCandidate>> GetScoredCandidatesAsync(
        Guid rideId, int regionId, double w1, double w2, double w3,
        CancellationToken cancellationToken = default)
    {
        // Start with 3km radius; expand to 5km if no candidates found
        var nearby = (await _geo.GetNearbyDriversAsync(0, 0, regionId, 3.0, cancellationToken)).ToList();
        if (!nearby.Any())
        {
            _logger.LogWarning("No drivers within 3km for region {RegionId}, expanding to 5km", regionId);
            nearby = (await _geo.GetNearbyDriversAsync(0, 0, regionId, 5.0, cancellationToken)).ToList();
        }

        _logger.LogInformation("Found {Count} driver candidates for ride {RideId}", nearby.Count, rideId);

        return nearby.Select(d =>
        {
            var proximity = 1.0 / (1.0 + d.DistanceKm);
            const double rating = 1.0;       // placeholder until driver profile module exists
            const double availability = 1.0; // placeholder
            var score = w1 * proximity + w2 * rating + w3 * availability;
            return new DriverCandidate(d.DriverId, score);
        });
    }
}
