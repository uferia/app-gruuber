using Gruuber.Rides.Domain;
using Gruuber.SharedKernel.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gruuber.Rides.Infrastructure;

/// <summary>
/// Unit of Work (concrete) — wraps RidesDbContext so that ride writes and outbox entries
/// always commit atomically in a single transaction.
/// </summary>
public sealed class RidesUnitOfWork(RidesDbContext context) : IUnitOfWork
{
    private IDbContextTransaction? _transaction;

    public DbSet<Ride> Rides => context.Rides;
    public DbSet<RideOutboxEntry> Outbox => context.Set<RideOutboxEntry>();

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        _transaction = await context.Database.BeginTransactionAsync(cancellationToken);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null) throw new InvalidOperationException("No active transaction.");
        await _transaction.CommitAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null) return;
        await _transaction.RollbackAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }
}
