using Gruuber.Api.Extensions;
using Gruuber.Auth.Application;
using Gruuber.Auth.Application.Commands;
using Microsoft.AspNetCore.Mvc;

namespace Gruuber.Api.Controllers;

[ApiController]
[Route("v1/auth")]
public class AuthController : ControllerBase
{
    private readonly LoginHandler _loginHandler;
    private readonly RefreshTokenHandler _refreshHandler;

    public AuthController(LoginHandler loginHandler, RefreshTokenHandler refreshHandler)
    {
        _loginHandler = loginHandler;
        _refreshHandler = refreshHandler;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginCommand command, CancellationToken cancellationToken)
    {
        var result = await _loginHandler.HandleAsync(command, cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshCommand command, CancellationToken cancellationToken)
    {
        var result = await _refreshHandler.HandleAsync(command, cancellationToken);
        return result.ToHttpResult(this);
    }
}
