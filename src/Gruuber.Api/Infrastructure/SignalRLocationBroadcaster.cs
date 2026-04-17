using Gruuber.Api.Hubs;
using Gruuber.Tracking.Application;
using Microsoft.AspNetCore.SignalR;

namespace Gruuber.Api.Infrastructure;

public class SignalRLocationBroadcaster : ILocationBroadcaster
{
    private readonly IHubContext<LocationHub> _hubContext;

    public SignalRLocationBroadcaster(IHubContext<LocationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task BroadcastDriverLocationAsync(Guid rideId, Guid driverId, double lat, double lng, CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients
            .Group(rideId.ToString())
            .SendAsync("DriverLocationUpdated", new
            {
                DriverId = driverId,
                Lat = lat,
                Lng = lng,
                Timestamp = DateTime.UtcNow
            }, cancellationToken);
    }
}
