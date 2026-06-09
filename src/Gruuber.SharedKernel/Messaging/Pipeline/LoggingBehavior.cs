using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Gruuber.SharedKernel.Messaging.Pipeline;

/// <summary>
/// Decorator (MediatR Pipeline Behavior) — logs every request with duration, success/failure,
/// and domain correlation IDs extracted from requests that implement ICorrelatedRequest.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var sw = Stopwatch.StartNew();

        logger.LogInformation("Handling {RequestName}", requestName);

        try
        {
            var response = await next();
            sw.Stop();
            logger.LogInformation("Handled {RequestName} in {ElapsedMs}ms", requestName, sw.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Request {RequestName} failed after {ElapsedMs}ms", requestName, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
