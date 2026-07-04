using System.ComponentModel.DataAnnotations;
using Gruuber.Api.Extensions;
using Gruuber.Restaurants.Application.Commands;
using Gruuber.Restaurants.Application.Queries;
using Gruuber.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gruuber.Api.Controllers;

[ApiController]
[Route("v1/restaurants")]
public class RestaurantsController : ControllerBase
{
    private readonly RegisterRestaurantHandler _registerHandler;
    private readonly RestaurantQueryHandler _queryHandler;
    private readonly ICurrentUserContext _currentUser;

    public RestaurantsController(
        RegisterRestaurantHandler registerHandler,
        RestaurantQueryHandler queryHandler,
        ICurrentUserContext currentUser)
    {
        _registerHandler = registerHandler;
        _queryHandler = queryHandler;
        _currentUser = currentUser;
    }

    [HttpPost("register")]
    [Authorize(Policy = "restaurant")]
    public async Task<IActionResult> Register([FromBody] RegisterRestaurantRequest request, CancellationToken cancellationToken)
    {
        var cmd = new RegisterRestaurantCommand(
            _currentUser.UserId, request.Name, request.Description ?? string.Empty, request.CuisineType,
            request.Address, request.Lat, request.Lng, _currentUser.RegionId);
        var result = await _registerHandler.HandleAsync(cmd, cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpGet("mine")]
    [Authorize(Policy = "restaurant")]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        var result = await _queryHandler.GetMineAsync(_currentUser.UserId, cancellationToken);
        return result.ToHttpResult(this);
    }
}

public record RegisterRestaurantRequest(
    [Required][StringLength(200, MinimumLength = 1)] string Name,
    [StringLength(2000)] string? Description,
    [Required][StringLength(100, MinimumLength = 1)] string CuisineType,
    [Required][StringLength(500, MinimumLength = 1)] string Address,
    [Range(-90, 90)] double Lat,
    [Range(-180, 180)] double Lng);
