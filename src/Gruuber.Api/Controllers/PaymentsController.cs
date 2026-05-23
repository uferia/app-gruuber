using System.ComponentModel.DataAnnotations;
using Gruuber.Api.Extensions;
using Gruuber.Payments.Application.Commands;
using Gruuber.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gruuber.Api.Controllers;

[ApiController]
[Route("v1/payments")]
public class PaymentsController : ControllerBase
{
    private readonly InitiatePaymentHandler _initiateHandler;
    private readonly ConfirmPaymentHandler _confirmHandler;
    private readonly FailPaymentHandler _failHandler;
    private readonly GetPaymentHandler _getHandler;
    private readonly ICurrentUserContext _currentUser;

    public PaymentsController(
        InitiatePaymentHandler initiateHandler,
        ConfirmPaymentHandler confirmHandler,
        FailPaymentHandler failHandler,
        GetPaymentHandler getHandler,
        ICurrentUserContext currentUser)
    {
        _initiateHandler = initiateHandler;
        _confirmHandler = confirmHandler;
        _failHandler = failHandler;
        _getHandler = getHandler;
        _currentUser = currentUser;
    }

    [HttpPost]
    [Authorize(Policy = "rider")]
    public async Task<IActionResult> Initiate([FromBody] InitiatePaymentRequest request, CancellationToken cancellationToken)
    {
        var cmd = new InitiatePaymentCommand(request.RideId, _currentUser.UserId, request.Amount, request.Currency, _currentUser.RegionId);
        var result = await _initiateHandler.HandleAsync(cmd, cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpPost("{id:guid}/confirm")]
    [Authorize(Policy = "driver")]
    public async Task<IActionResult> Confirm(Guid id, [FromBody] PaymentVersionRequest request, CancellationToken cancellationToken)
    {
        var result = await _confirmHandler.HandleAsync(new ConfirmPaymentCommand(id, request.ExpectedVersion, request.RegionId), cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpPost("{id:guid}/fail")]
    [Authorize(Policy = "driver")]
    public async Task<IActionResult> Fail(Guid id, [FromBody] FailPaymentRequest request, CancellationToken cancellationToken)
    {
        var result = await _failHandler.HandleAsync(new FailPaymentCommand(id, request.ExpectedVersion, request.Reason, request.RegionId), cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetPayment(Guid id, CancellationToken cancellationToken)
    {
        var result = await _getHandler.HandleAsync(new GetPaymentQuery(id), cancellationToken);
        return result.ToHttpResult(this);
    }
}

public record InitiatePaymentRequest(
    Guid RideId,
    Guid RiderId,
    [Range(0.01, 1000000.0)] decimal Amount,
    [Required][StringLength(3, MinimumLength = 3)] string Currency,
    int RegionId);
public record PaymentVersionRequest(
    [Range(1, long.MaxValue)] long ExpectedVersion,
    int RegionId);
public record FailPaymentRequest(
    [Range(1, long.MaxValue)] long ExpectedVersion,
    [Required] string Reason,
    int RegionId);
