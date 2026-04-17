namespace Gruuber.Tracking.Application;

public interface IGeoService
{
    Task AddDriverLocationAsync(Guid driverId, double lat, double lng, int regionId, CancellationToken cancellationToken = default);
    Task<IEnumerable<NearbyDriver>> GetNearbyDriversAsync(double lat, double lng, int regionId, double radiusKm = 5.0, CancellationToken cancellationToken = default);
    Task RemoveDriverAsync(Guid driverId, int regionId, CancellationToken cancellationToken = default);
}

public record NearbyDriver(Guid DriverId, double DistanceKm);
