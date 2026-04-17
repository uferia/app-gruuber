using Gruuber.Api.Extensions;
using Gruuber.Payments.Application.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gruuber.Api.Controllers;

[ApiController]
[Route("v1/payments")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly InitiatePaymentHandler _initiateHandler;
    private readonly ConfirmPaymentHandler _confirmHandler;
    private readonly FailPaymentHandler _failHandler;

    public PaymentsController(
        InitiatePaymentHandler initiateHandler,
        ConfirmPaymentHandler confirmHandler,
        FailPaymentHandler failHandler)
    {
        _initiateHandler = initiateHandler;
        _confirmHandler = confirmHandler;
        _failHandler = failHandler;
    }

    [HttpPost]
    public async Task<IActionResult> Initiate([FromBody] InitiatePaymentRequest request, CancellationToken cancellationToken)
    {
        var cmd = new InitiatePaymentCommand(request.RideId, request.RiderId, request.Amount, request.Currency, request.RegionId);
        var result = await _initiateHandler.HandleAsync(cmd, cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, [FromBody] PaymentVersionRequest request, CancellationToken cancellationToken)
    {
        var result = await _confirmHandler.HandleAsync(new ConfirmPaymentCommand(id, request.ExpectedVersion, request.RegionId), cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpPost("{id:guid}/fail")]
    public async Task<IActionResult> Fail(Guid id, [FromBody] FailPaymentRequest request, CancellationToken cancellationToken)
    {
        var result = await _failHandler.HandleAsync(new FailPaymentCommand(id, request.ExpectedVersion, request.Reason, request.RegionId), cancellationToken);
        return result.ToHttpResult(this);
    }
}

public record InitiatePaymentRequest(Guid RideId, Guid RiderId, decimal Amount, string Currency, int RegionId);
public record PaymentVersionRequest(long ExpectedVersion, int RegionId);
public record FailPaymentRequest(long ExpectedVersion, string Reason, int RegionId);
