namespace Gruuber.Tracking.Application;

public interface ILocationBroadcaster
{
    Task BroadcastDriverLocationAsync(Guid rideId, Guid driverId, double lat, double lng, CancellationToken cancellationToken = default);
}
