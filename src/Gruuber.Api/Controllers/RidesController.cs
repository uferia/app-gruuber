using System.ComponentModel.DataAnnotations;
using Gruuber.Api.Extensions;
using Gruuber.Rides.Application.Commands;
using Gruuber.Rides.Application.Queries;
using Gruuber.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gruuber.Api.Controllers;

[ApiController]
[Route("v1/rides")]
public class RidesController : ControllerBase
{
    private readonly RequestRideHandler _requestHandler;
    private readonly MatchDriverHandler _matchHandler;
    private readonly GetRideStatusHandler _statusHandler;
    private readonly TransitionRideHandler _transitionHandler;
    private readonly ICurrentUserContext _currentUser;

    public RidesController(
        RequestRideHandler requestHandler,
        MatchDriverHandler matchHandler,
        GetRideStatusHandler statusHandler,
        TransitionRideHandler transitionHandler,
        ICurrentUserContext currentUser)
    {
        _requestHandler = requestHandler;
        _matchHandler = matchHandler;
        _statusHandler = statusHandler;
        _transitionHandler = transitionHandler;
        _currentUser = currentUser;
    }

    [HttpPost("request")]
    [Authorize(Policy = "rider")]
    public async Task<IActionResult> RequestRide([FromBody] RequestRideRequest request, CancellationToken cancellationToken)
    {
        var cmd = new RequestRideCommand(_currentUser.UserId, request.RideType, request.PickupLat, request.PickupLng, _currentUser.RegionId);
        var result = await _requestHandler.HandleAsync(cmd, cancellationToken);

        return result.ToHttpResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetRideStatus(Guid id, CancellationToken cancellationToken)
    {
        var result = await _statusHandler.HandleAsync(new GetRideStatusQuery(id), cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpPost("{id:guid}/match")]
    [Authorize(Policy = "driver")]
    public async Task<IActionResult> MatchRide(Guid id, [FromBody] MatchRideRequest request, CancellationToken cancellationToken)
    {
        var cmd = new MatchDriverCommand(id, request.ExpectedVersion, _currentUser.RegionId);
        var result = await _matchHandler.HandleAsync(cmd, cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize]
    public async Task<IActionResult> TransitionStatus(Guid id, [FromBody] TransitionRideRequest request, CancellationToken cancellationToken)
    {
        var cmd = new TransitionRideCommand(id, request.NewStatus, request.ExpectedVersion, _currentUser.RegionId, _currentUser.UserId);
        var result = await _transitionHandler.HandleAsync(cmd, cancellationToken);
        return result.ToHttpResult(this);
    }
}

public record RequestRideRequest(
    Guid RiderId,
    [Required][StringLength(64, MinimumLength = 1)] string RideType,
    [Range(-90.0, 90.0)] double PickupLat,
    [Range(-180.0, 180.0)] double PickupLng,
    int RegionId);
public record MatchRideRequest([Range(1, long.MaxValue)] long ExpectedVersion);
public record TransitionRideRequest(
    [Required] string NewStatus,
    [Range(1, long.MaxValue)] long ExpectedVersion);
