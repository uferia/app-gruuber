using System.ComponentModel.DataAnnotations;
using Gruuber.Api.Extensions;
using Gruuber.Orders.Application.Commands;
using Gruuber.Orders.Application.Queries;
using Gruuber.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gruuber.Api.Controllers;

[ApiController]
[Route("v1/orders")]
public class OrdersController : ControllerBase
{
    private readonly CreateOrderHandler _createHandler;
    private readonly TransitionOrderHandler _transitionHandler;
    private readonly GetOrderHandler _getHandler;
    private readonly GetOrderItemsHandler _getItemsHandler;
    private readonly ICurrentUserContext _currentUser;

    public OrdersController(
        CreateOrderHandler createHandler,
        TransitionOrderHandler transitionHandler,
        GetOrderHandler getHandler,
        GetOrderItemsHandler getItemsHandler,
        ICurrentUserContext currentUser)
    {
        _createHandler = createHandler;
        _transitionHandler = transitionHandler;
        _getHandler = getHandler;
        _getItemsHandler = getItemsHandler;
        _currentUser = currentUser;
    }

    [HttpPost("create")]
    [Authorize(Policy = "rider")]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var cmd = new CreateOrderCommand(
            _currentUser.UserId, request.RestaurantId, request.RideId, _currentUser.RegionId,
            request.Items.Select(i => new OrderItemRequest(i.MenuItemId, i.Quantity, i.Price)).ToList());

        var result = await _createHandler.HandleAsync(cmd, cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize]
    public async Task<IActionResult> TransitionStatus(Guid id, [FromBody] TransitionOrderRequest request, CancellationToken cancellationToken)
    {
        var cmd = new TransitionOrderCommand(id, request.NewStatus, request.ExpectedVersion, _currentUser.RegionId);
        var result = await _transitionHandler.HandleAsync(cmd, cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetOrder(Guid id, CancellationToken cancellationToken)
    {
        var result = await _getHandler.HandleAsync(new GetOrderQuery(id), cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpGet("{id:guid}/items")]
    [Authorize]
    public async Task<IActionResult> GetOrderItems(Guid id, CancellationToken cancellationToken)
    {
        var result = await _getItemsHandler.HandleAsync(new GetOrderItemsQuery(id), cancellationToken);
        return result.ToHttpResult(this);
    }
}

public record CreateOrderRequest(
    Guid RiderId,
    [Required] Guid RestaurantId,
    [Required] Guid RideId,
    int RegionId,
    [Required][MinLength(1)] IList<OrderItemInput> Items);
public record OrderItemInput(
    [Required] Guid MenuItemId,
    [Range(1, 1000)] int Quantity,
    [Range(0.01, 100000.0)] decimal Price);
public record TransitionOrderRequest(
    [Required] string NewStatus,
    [Range(1, long.MaxValue)] long ExpectedVersion,
    int RegionId);
