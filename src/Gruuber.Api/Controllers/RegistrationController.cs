using Gruuber.Api.Extensions;
using Gruuber.Auth.Application;
using Gruuber.Auth.Application.Commands;
using Microsoft.AspNetCore.Mvc;

namespace Gruuber.Api.Controllers;

[ApiController]
[Route("v1/auth")]
public class RegistrationController : ControllerBase
{
    private readonly ICommandHandler<RegisterCommand, RegisterResponse> _registerHandler;
    private readonly ICommandHandler<RegisterRiderCommand, RegisterRiderResponse> _registerRiderHandler;
    private readonly ICommandHandler<RegisterDriverCommand, RegisterDriverResponse> _registerDriverHandler;

    public RegistrationController(
        ICommandHandler<RegisterCommand, RegisterResponse> registerHandler,
        ICommandHandler<RegisterRiderCommand, RegisterRiderResponse> registerRiderHandler,
        ICommandHandler<RegisterDriverCommand, RegisterDriverResponse> registerDriverHandler)
    {
        _registerHandler = registerHandler;
        _registerRiderHandler = registerRiderHandler;
        _registerDriverHandler = registerDriverHandler;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterCommand command, CancellationToken cancellationToken)
    {
        var result = await _registerHandler.HandleAsync(command, cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpPost("register/rider")]
    public async Task<IActionResult> RegisterRider([FromBody] RegisterRiderCommand command, CancellationToken cancellationToken)
    {
        var result = await _registerRiderHandler.HandleAsync(command, cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpPost("register/driver")]
    public async Task<IActionResult> RegisterDriver([FromBody] RegisterDriverCommand command, CancellationToken cancellationToken)
    {
        var result = await _registerDriverHandler.HandleAsync(command, cancellationToken);
        return result.ToHttpResult(this);
    }
}
