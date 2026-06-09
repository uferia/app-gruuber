using System.ComponentModel.DataAnnotations;
using MediatR;
using Gruuber.SharedKernel.Results;

namespace Gruuber.SharedKernel.Messaging.Pipeline;

/// <summary>
/// Decorator (MediatR Pipeline Behavior) — validates request objects annotated with
/// System.ComponentModel.DataAnnotations attributes before the handler executes.
/// Returns ApplicationResult failure on validation errors instead of throwing.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, context, results, validateAllProperties: true))
        {
            var message = string.Join("; ", results.Select(r => r.ErrorMessage));

            var responseType = typeof(TResponse);
            if (responseType.IsGenericType &&
                responseType.GetGenericTypeDefinition() == typeof(ApplicationResult<>))
            {
                var method = responseType.GetMethod(nameof(ApplicationResult<object>.Failure),
                    [typeof(string), typeof(string), typeof(int)])!;
                var result = method.Invoke(null, ["VALIDATION_FAILED", message, 400]);
                return Task.FromResult((TResponse)result!);
            }

            throw new ValidationException(message);
        }

        return next();
    }
}
