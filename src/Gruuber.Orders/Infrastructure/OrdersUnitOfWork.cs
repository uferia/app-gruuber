using Gruuber.Orders.Domain;
using Gruuber.SharedKernel.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gruuber.Orders.Infrastructure;

/// <summary>
/// Unit of Work (concrete) — wraps OrdersDbContext so that order writes and outbox entries
/// always commit atomically in a single transaction.
/// </summary>
public sealed class OrdersUnitOfWork(OrdersDbContext context) : IUnitOfWork
{
    private IDbContextTransaction? _transaction;

    public DbSet<Order> Orders => context.Orders;
    public DbSet<OrderOutboxEntry> Outbox => context.Set<OrderOutboxEntry>();

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
