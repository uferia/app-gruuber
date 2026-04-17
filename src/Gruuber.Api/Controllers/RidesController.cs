using Gruuber.Api.Extensions;
using Gruuber.Rides.Application.Commands;
using Gruuber.Rides.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gruuber.Api.Controllers;

[ApiController]
[Route("v1/rides")]
[Authorize]
public class RidesController : ControllerBase
{
    private readonly RequestRideHandler _requestHandler;
    private readonly MatchDriverHandler _matchHandler;
    private readonly GetRideStatusHandler _statusHandler;

    public RidesController(
        RequestRideHandler requestHandler,
        MatchDriverHandler matchHandler,
        GetRideStatusHandler statusHandler)
    {
        _requestHandler = requestHandler;
        _matchHandler = matchHandler;
        _statusHandler = statusHandler;
    }

    [HttpPost("request")]
    public async Task<IActionResult> RequestRide([FromBody] RequestRideRequest request, CancellationToken cancellationToken)
    {
        var cmd = new RequestRideCommand(request.RiderId, request.RideType, request.PickupLat, request.PickupLng, request.RegionId);
        var result = await _requestHandler.HandleAsync(cmd, cancellationToken);

        return result.ToHttpResult(this);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetRideStatus(Guid id, CancellationToken cancellationToken)
    {
        var result = await _statusHandler.HandleAsync(new GetRideStatusQuery(id), cancellationToken);
        return result.ToHttpResult(this);
    }
}

public record RequestRideRequest(Guid RiderId, string RideType, double PickupLat, double PickupLng, int RegionId);
