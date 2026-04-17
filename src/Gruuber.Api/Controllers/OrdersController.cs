using Gruuber.Api.Extensions;
using Gruuber.Orders.Application.Commands;
using Gruuber.Orders.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gruuber.Api.Controllers;

[ApiController]
[Route("v1/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly CreateOrderHandler _createHandler;
    private readonly TransitionOrderHandler _transitionHandler;
    private readonly GetOrderHandler _getHandler;

    public OrdersController(
        CreateOrderHandler createHandler,
        TransitionOrderHandler transitionHandler,
        GetOrderHandler getHandler)
    {
        _createHandler = createHandler;
        _transitionHandler = transitionHandler;
        _getHandler = getHandler;
    }

    [HttpPost("create")]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var cmd = new CreateOrderCommand(
            request.RiderId, request.RestaurantId, request.RideId, request.RegionId,
            request.Items.Select(i => new OrderItemRequest(i.MenuItemId, i.Quantity, i.Price)).ToList());

        var result = await _createHandler.HandleAsync(cmd, cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> TransitionStatus(Guid id, [FromBody] TransitionOrderRequest request, CancellationToken cancellationToken)
    {
        var cmd = new TransitionOrderCommand(id, request.NewStatus, request.ExpectedVersion, request.RegionId);
        var result = await _transitionHandler.HandleAsync(cmd, cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetOrder(Guid id, CancellationToken cancellationToken)
    {
        var result = await _getHandler.HandleAsync(new GetOrderQuery(id), cancellationToken);
        return result.ToHttpResult(this);
    }
}

public record CreateOrderRequest(Guid RiderId, Guid RestaurantId, Guid RideId, int RegionId, IList<OrderItemInput> Items);
public record OrderItemInput(Guid MenuItemId, int Quantity, decimal Price);
public record TransitionOrderRequest(string NewStatus, long ExpectedVersion, int RegionId);
