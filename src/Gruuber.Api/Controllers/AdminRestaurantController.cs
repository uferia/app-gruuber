using System.ComponentModel.DataAnnotations;
using Gruuber.Api.Extensions;
using Gruuber.Restaurants.Application.Commands;
using Gruuber.Restaurants.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gruuber.Api.Controllers;

[ApiController]
[Route("v1/admin/restaurants")]
[Authorize(Roles = "admin")]
public class AdminRestaurantController : ControllerBase
{
    private readonly ApproveRestaurantHandler _approveHandler;
    private readonly RejectRestaurantHandler _rejectHandler;
    private readonly RestaurantQueryHandler _queryHandler;

    public AdminRestaurantController(
        ApproveRestaurantHandler approveHandler,
        RejectRestaurantHandler rejectHandler,
        RestaurantQueryHandler queryHandler)
    {
        _approveHandler = approveHandler;
        _rejectHandler = rejectHandler;
        _queryHandler = queryHandler;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await _queryHandler.GetAdminListAsync(status, page, pageSize, cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpPost("{restaurantId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid restaurantId, [FromBody] ApproveRestaurantRequest request, CancellationToken cancellationToken)
    {
        var result = await _approveHandler.HandleAsync(
            new ApproveRestaurantCommand(restaurantId, request.ExpectedVersion), cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpPost("{restaurantId:guid}/reject")]
    public async Task<IActionResult> Reject(Guid restaurantId, [FromBody] RejectRestaurantRequest request, CancellationToken cancellationToken)
    {
        var result = await _rejectHandler.HandleAsync(
            new RejectRestaurantCommand(restaurantId, request.ExpectedVersion, request.Reason), cancellationToken);
        return result.ToHttpResult(this);
    }
}

public record ApproveRestaurantRequest(long ExpectedVersion);
public record RejectRestaurantRequest(long ExpectedVersion, [Required][StringLength(500, MinimumLength = 1)] string Reason);
