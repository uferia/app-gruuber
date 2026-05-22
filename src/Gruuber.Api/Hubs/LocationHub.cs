using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Gruuber.Api.Hubs;

[Authorize]
public class LocationHub : Hub
{
    public async Task JoinRideGroup(string rideId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, rideId);
    }

    public async Task LeaveRideGroup(string rideId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, rideId);
    }
}
