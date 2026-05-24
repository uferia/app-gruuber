using Gruuber.Api.Extensions;
using Gruuber.Auth.Application.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gruuber.Api.Controllers;

[ApiController]
[Route("v1/admin/drivers")]
[Authorize(Roles = "admin")]
public class AdminDriverController : ControllerBase
{
    private readonly ApproveDriverHandler _approveHandler;
    private readonly RejectDriverHandler _rejectHandler;

    public AdminDriverController(ApproveDriverHandler approveHandler, RejectDriverHandler rejectHandler)
    {
        _approveHandler = approveHandler;
        _rejectHandler = rejectHandler;
    }

    [HttpPost("{driverProfileId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid driverProfileId, [FromBody] ApproveDriverRequest request, CancellationToken cancellationToken)
    {
        var result = await _approveHandler.HandleAsync(new ApproveDriverCommand(driverProfileId, request.ExpectedVersion), cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpPost("{driverProfileId:guid}/reject")]
    public async Task<IActionResult> Reject(Guid driverProfileId, [FromBody] RejectDriverRequest request, CancellationToken cancellationToken)
    {
        var result = await _rejectHandler.HandleAsync(new RejectDriverCommand(driverProfileId, request.ExpectedVersion, request.Reason), cancellationToken);
        return result.ToHttpResult(this);
    }
}

public record ApproveDriverRequest(long ExpectedVersion);
public record RejectDriverRequest(long ExpectedVersion, string Reason);
