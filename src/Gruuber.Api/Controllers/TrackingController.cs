using Gruuber.Api.Extensions;
using Gruuber.Tracking.Application.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gruuber.Api.Controllers;

[ApiController]
[Route("v1/drivers")]
[Authorize]
public class TrackingController : ControllerBase
{
    private readonly UpdateDriverLocationHandler _handler;

    public TrackingController(UpdateDriverLocationHandler handler)
    {
        _handler = handler;
    }

    [HttpPost("location")]
    public async Task<IActionResult> UpdateLocation([FromBody] UpdateLocationRequest request, CancellationToken cancellationToken)
    {
        var cmd = new UpdateDriverLocationCommand(request.DriverId, request.Lat, request.Lng, request.RegionId, request.ActiveRideId);
        var result = await _handler.HandleAsync(cmd, cancellationToken);
        return result.ToHttpResult(this);
    }
}

public record UpdateLocationRequest(Guid DriverId, double Lat, double Lng, int RegionId, Guid? ActiveRideId);
