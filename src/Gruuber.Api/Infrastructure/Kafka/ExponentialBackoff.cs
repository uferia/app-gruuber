using Gruuber.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace Gruuber.Api.Infrastructure.Kafka;

public class ExponentialBackoff : IExponentialBackoff
{
    private readonly ILogger<ExponentialBackoff> _logger;

    public ExponentialBackoff(ILogger<ExponentialBackoff> logger)
    {
        _logger = logger;
    }

    public async Task<bool> ExecuteWithRetryAsync(
        Func<Task> operation,
        int maxRetries = 5,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await operation();
                return true;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                var jitter = Random.Shared.Next(0, 500);
                var delay = TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 100 + jitter);
                _logger.LogWarning(ex, "Attempt {Attempt}/{MaxRetries} failed. Retrying in {DelayMs}ms",
                    attempt, maxRetries, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "All {MaxRetries} retry attempts exhausted", maxRetries);
                return false;
            }
        }
        return false;
    }
}
