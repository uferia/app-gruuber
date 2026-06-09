using MediatR;
using Microsoft.Extensions.Logging;
using Gruuber.SharedKernel.Results;

namespace Gruuber.SharedKernel.Messaging.Pipeline;

/// <summary>
/// Decorator (MediatR Pipeline Behavior) — catches unhandled exceptions and maps them
/// to ApplicationResult failures, preventing raw exceptions from bubbling to controllers.
/// Only wraps responses whose TResponse is ApplicationResult&lt;T&gt;.
/// </summary>
public sealed class ErrorHandlingBehavior<TRequest, TResponse>(
    Microsoft.Extensions.Logging.ILogger<ErrorHandlingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (OperationCanceledException)
        {
            throw; // propagate cancellation
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception in {RequestName}", typeof(TRequest).Name);

            // If TResponse is ApplicationResult<T>, wrap into a failure response.
            var responseType = typeof(TResponse);
            if (responseType.IsGenericType &&
                responseType.GetGenericTypeDefinition() == typeof(ApplicationResult<>))
            {
                var inner = responseType.GetGenericArguments()[0];
                var method = responseType.GetMethod(nameof(ApplicationResult<object>.Failure),
                    [typeof(string), typeof(string), typeof(int)])!;
                var result = method.Invoke(null, ["INTERNAL_ERROR", "An unexpected error occurred.", 500]);
                return (TResponse)result!;
            }

            throw;
        }
    }
}
