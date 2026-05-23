namespace Gruuber.Rides.Application.Commands;

public interface IDriverScoringService
{
    Task<IEnumerable<DriverCandidate>> GetScoredCandidatesAsync(
        Guid rideId, int regionId, double w1, double w2, double w3,
        double pickupLat, double pickupLng,
        CancellationToken cancellationToken = default);
}

public record DriverCandidate(Guid DriverId, double Score);
