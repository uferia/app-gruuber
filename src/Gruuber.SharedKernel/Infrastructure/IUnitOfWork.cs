namespace Gruuber.SharedKernel.Infrastructure;

/// <summary>
/// Unit of Work pattern — wraps a DbContext and the outbox table so that domain writes
/// and event publishing always commit atomically in a single transaction.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
