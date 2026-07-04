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
    private readonly UpdateRestaurantHandler _updateHandler;
    private readonly SetRestaurantOpenHandler _setOpenHandler;
    private readonly AddMenuItemHandler _addMenuItemHandler;
    private readonly UpdateMenuItemHandler _updateMenuItemHandler;
    private readonly DeleteMenuItemHandler _deleteMenuItemHandler;
    private readonly ICurrentUserContext _currentUser;

    public RestaurantsController(
        RegisterRestaurantHandler registerHandler,
        RestaurantQueryHandler queryHandler,
        UpdateRestaurantHandler updateHandler,
        SetRestaurantOpenHandler setOpenHandler,
        AddMenuItemHandler addMenuItemHandler,
        UpdateMenuItemHandler updateMenuItemHandler,
        DeleteMenuItemHandler deleteMenuItemHandler,
        ICurrentUserContext currentUser)
    {
        _registerHandler = registerHandler;
        _queryHandler = queryHandler;
        _updateHandler = updateHandler;
        _setOpenHandler = setOpenHandler;
        _addMenuItemHandler = addMenuItemHandler;
        _updateMenuItemHandler = updateMenuItemHandler;
        _deleteMenuItemHandler = deleteMenuItemHandler;
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

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Discover(
        [FromQuery] double? lat, [FromQuery] double? lng, [FromQuery] string? search,
        [FromQuery] bool openNow = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new DiscoverRestaurantsQuery(_currentUser.RegionId, lat, lng, search, openNow, page, pageSize);
        var result = await _queryHandler.DiscoverAsync(query, cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _queryHandler.GetPublicAsync(id, cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpGet("{id:guid}/menu")]
    [Authorize]
    public async Task<IActionResult> GetMenu(Guid id, CancellationToken cancellationToken)
    {
        var result = await _queryHandler.GetMenuAsync(id, cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "restaurant")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRestaurantRequest request, CancellationToken cancellationToken)
    {
        var cmd = new UpdateRestaurantCommand(
            id, _currentUser.UserId, request.ExpectedVersion, request.Name, request.Description ?? string.Empty,
            request.CuisineType, request.Address, request.Lat, request.Lng);
        var result = await _updateHandler.HandleAsync(cmd, cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpPatch("{id:guid}/open")]
    [Authorize(Policy = "restaurant")]
    public async Task<IActionResult> SetOpen(Guid id, [FromBody] SetOpenRequest request, CancellationToken cancellationToken)
    {
        var cmd = new SetRestaurantOpenCommand(id, _currentUser.UserId, request.ExpectedVersion, request.IsOpen);
        var result = await _setOpenHandler.HandleAsync(cmd, cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpPost("{id:guid}/menu-items")]
    [Authorize(Policy = "restaurant")]
    public async Task<IActionResult> AddMenuItem(Guid id, [FromBody] AddMenuItemRequest request, CancellationToken cancellationToken)
    {
        var cmd = new AddMenuItemCommand(
            id, _currentUser.UserId, request.Name, request.Description ?? string.Empty,
            request.Category ?? string.Empty, request.Price, request.Currency ?? "USD");
        var result = await _addMenuItemHandler.HandleAsync(cmd, cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpPatch("{id:guid}/menu-items/{itemId:guid}")]
    [Authorize(Policy = "restaurant")]
    public async Task<IActionResult> UpdateMenuItem(Guid id, Guid itemId, [FromBody] UpdateMenuItemRequest request, CancellationToken cancellationToken)
    {
        var cmd = new UpdateMenuItemCommand(
            id, itemId, _currentUser.UserId, request.ExpectedVersion, request.Name,
            request.Description ?? string.Empty, request.Category ?? string.Empty, request.Price, request.IsAvailable);
        var result = await _updateMenuItemHandler.HandleAsync(cmd, cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpDelete("{id:guid}/menu-items/{itemId:guid}")]
    [Authorize(Policy = "restaurant")]
    public async Task<IActionResult> DeleteMenuItem(Guid id, Guid itemId, CancellationToken cancellationToken)
    {
        var result = await _deleteMenuItemHandler.HandleAsync(
            new DeleteMenuItemCommand(id, itemId, _currentUser.UserId), cancellationToken);
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

public record UpdateRestaurantRequest(
    long ExpectedVersion,
    [Required][StringLength(200, MinimumLength = 1)] string Name,
    [StringLength(2000)] string? Description,
    [Required][StringLength(100, MinimumLength = 1)] string CuisineType,
    [Required][StringLength(500, MinimumLength = 1)] string Address,
    [Range(-90, 90)] double Lat,
    [Range(-180, 180)] double Lng);

public record SetOpenRequest(long ExpectedVersion, bool IsOpen);

public record AddMenuItemRequest(
    [Required][StringLength(200, MinimumLength = 1)] string Name,
    [StringLength(1000)] string? Description,
    [StringLength(100)] string? Category,
    [Range(0.01, 1000000)] decimal Price,
    [StringLength(3, MinimumLength = 3)] string? Currency);

public record UpdateMenuItemRequest(
    long ExpectedVersion,
    [Required][StringLength(200, MinimumLength = 1)] string Name,
    [StringLength(1000)] string? Description,
    [StringLength(100)] string? Category,
    [Range(0.01, 1000000)] decimal Price,
    bool IsAvailable);
