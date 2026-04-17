using Gruuber.SharedKernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace Gruuber.Api.Extensions;

public static class ApplicationResultExtensions
{
    public static IActionResult ToHttpResult<T>(this ApplicationResult<T> result, ControllerBase controller)
    {
        if (result.IsSuccess)
        {
            return result.StatusCode switch
            {
                202 => controller.Accepted(result.Data),
                201 => controller.Created(string.Empty, result.Data),
                _ => controller.Ok(result.Data)
            };
        }

        var error = new { result.ErrorCode, result.ErrorMessage };
        return result.StatusCode switch
        {
            404 => controller.NotFound(error),
            409 => controller.Conflict(error),
            _ => controller.BadRequest(error)
        };
    }
}
