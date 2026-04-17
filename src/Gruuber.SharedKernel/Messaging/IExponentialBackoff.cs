namespace Gruuber.SharedKernel.Messaging;

public interface IExponentialBackoff
{
    Task<bool> ExecuteWithRetryAsync(
        Func<Task> operation,
        int maxRetries = 5,
        CancellationToken cancellationToken = default);
}
