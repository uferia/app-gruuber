using System.ComponentModel.DataAnnotations;
using Gruuber.Api.Extensions;
using Gruuber.Orders.Application.Commands;
using Gruuber.Orders.Application.Queries;
using Gruuber.SharedKernel.Infrastructure;
using Gruuber.SharedKernel.Payments;
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
        if (!Enum.TryParse<PaymentMethod>(request.PaymentMethod, ignoreCase: true, out var method))
            return BadRequest(new { ErrorCode = "INVALID_PAYMENT_METHOD", ErrorMessage = "PaymentMethod must be CardMock or CashOnDelivery." });

        var cmd = new CreateOrderCommand(
            _currentUser.UserId, request.RestaurantId, _currentUser.RegionId,
            request.Items.Select(i => new OrderItemRequest(i.MenuItemId, i.Quantity)).ToList(),
            request.DeliveryLat, request.DeliveryLng, method);

        var result = await _createHandler.HandleAsync(cmd, cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize]
    public async Task<IActionResult> TransitionStatus(Guid id, [FromBody] TransitionOrderRequest request, CancellationToken cancellationToken)
    {
        var cmd = new TransitionOrderCommand(
            id, request.NewStatus, request.ExpectedVersion, _currentUser.RegionId,
            _currentUser.UserId, _currentUser.Role, request.Reason, request.Note);
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
    [Required] Guid RestaurantId,
    [Required][MinLength(1)] IList<OrderItemInput> Items,
    [Range(-90, 90)] double DeliveryLat,
    [Range(-180, 180)] double DeliveryLng,
    [Required] string PaymentMethod);
public record OrderItemInput(
    [Required] Guid MenuItemId,
    [Range(1, 100)] int Quantity);
public record TransitionOrderRequest(
    [Required] string NewStatus,
    [Range(1, long.MaxValue)] long ExpectedVersion,
    string? Reason = null,
    [StringLength(500)] string? Note = null);
