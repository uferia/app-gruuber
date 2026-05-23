using System.ComponentModel.DataAnnotations;
using Gruuber.Api.Extensions;
using Gruuber.SharedKernel.Infrastructure;
using Gruuber.Tracking.Application.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gruuber.Api.Controllers;

[ApiController]
[Route("v1/drivers")]
[Authorize(Policy = "driver")]
public class TrackingController : ControllerBase
{
    private readonly UpdateDriverLocationHandler _handler;
    private readonly ICurrentUserContext _currentUser;

    public TrackingController(UpdateDriverLocationHandler handler, ICurrentUserContext currentUser)
    {
        _handler = handler;
        _currentUser = currentUser;
    }

    [HttpPost("location")]
    public async Task<IActionResult> UpdateLocation([FromBody] UpdateLocationRequest request, CancellationToken cancellationToken)
    {
        var cmd = new UpdateDriverLocationCommand(_currentUser.UserId, request.Lat, request.Lng, _currentUser.RegionId, request.ActiveRideId);
        var result = await _handler.HandleAsync(cmd, cancellationToken);
        return result.ToHttpResult(this);
    }
}

public record UpdateLocationRequest(
    Guid DriverId,
    [Range(-90.0, 90.0)] double Lat,
    [Range(-180.0, 180.0)] double Lng,
    int RegionId,
    Guid? ActiveRideId);
