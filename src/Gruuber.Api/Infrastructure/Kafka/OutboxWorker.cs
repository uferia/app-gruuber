using Gruuber.Orders.Infrastructure;
using Gruuber.Payments.Infrastructure;
using Gruuber.Rides.Infrastructure;
using Gruuber.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gruuber.Api.Infrastructure.Kafka;

public class OutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IKafkaProducer _producer;
    private readonly IExponentialBackoff _backoff;
    private readonly ILogger<OutboxWorker> _logger;
    private const int MaxRetries = 5;

    public OutboxWorker(
        IServiceScopeFactory scopeFactory,
        IKafkaProducer producer,
        IExponentialBackoff backoff,
        ILogger<OutboxWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _producer = producer;
        _backoff = backoff;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OutboxWorker encountered an unexpected error");
            }

            await Task.Delay(500, stoppingToken);
        }
    }

    private async Task ProcessOutboxAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        await ProcessRidesOutboxAsync(scope, cancellationToken);
        await ProcessOrdersOutboxAsync(scope, cancellationToken);
        await ProcessPaymentsOutboxAsync(scope, cancellationToken);
    }

    private async Task ProcessRidesOutboxAsync(AsyncServiceScope scope, CancellationToken cancellationToken)
    {
        var db = scope.ServiceProvider.GetRequiredService<RidesDbContext>();
        var pending = await db.Set<RideOutboxEntry>()
            .Where(e => e.Status == "pending" && e.RetryCount < MaxRetries)
            .OrderBy(e => e.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var entry in pending)
        {
            var success = await _backoff.ExecuteWithRetryAsync(async () =>
            {
                await _producer.PublishAsync(entry.EventType, entry.Id.ToString(), entry.Payload, cancellationToken);
            }, cancellationToken: cancellationToken);

            if (success)
            {
                entry.Status = "processed";
                entry.ProcessedAt = DateTime.UtcNow;
            }
            else
            {
                entry.RetryCount++;
                if (entry.RetryCount >= MaxRetries)
                {
                    entry.Status = "dlq";
                    try
                    {
                        await _producer.PublishAsync(
                            $"{entry.EventType}-dlq",
                            entry.Id.ToString(),
                            entry.Payload,
                            cancellationToken);
                        _logger.LogError("Entry {Id} published to DLQ topic {Topic} after {Retries} retries",
                            entry.Id, $"{entry.EventType}-dlq", entry.RetryCount);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to publish entry {Id} to DLQ topic — entry marked dlq in DB only", entry.Id);
                    }
                }
                else
                {
                    entry.Status = "pending";
                    _logger.LogWarning("Entry {Id} retry {RetryCount}/{MaxRetries}", entry.Id, entry.RetryCount, MaxRetries);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessOrdersOutboxAsync(AsyncServiceScope scope, CancellationToken cancellationToken)
    {
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var pending = await db.Set<OrderOutboxEntry>()
            .Where(e => e.Status == "pending" && e.RetryCount < MaxRetries)
            .OrderBy(e => e.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var entry in pending)
        {
            var success = await _backoff.ExecuteWithRetryAsync(async () =>
            {
                await _producer.PublishAsync(entry.EventType, entry.Id.ToString(), entry.Payload, cancellationToken);
            }, cancellationToken: cancellationToken);

            if (success)
            {
                entry.Status = "processed";
                entry.ProcessedAt = DateTime.UtcNow;
            }
            else
            {
                entry.RetryCount++;
                if (entry.RetryCount >= MaxRetries)
                {
                    entry.Status = "dlq";
                    try
                    {
                        await _producer.PublishAsync(
                            $"{entry.EventType}-dlq",
                            entry.Id.ToString(),
                            entry.Payload,
                            cancellationToken);
                        _logger.LogError("Entry {Id} published to DLQ topic {Topic} after {Retries} retries",
                            entry.Id, $"{entry.EventType}-dlq", entry.RetryCount);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to publish entry {Id} to DLQ topic — entry marked dlq in DB only", entry.Id);
                    }
                }
                else
                {
                    entry.Status = "pending";
                    _logger.LogWarning("Entry {Id} retry {RetryCount}/{MaxRetries}", entry.Id, entry.RetryCount, MaxRetries);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessPaymentsOutboxAsync(AsyncServiceScope scope, CancellationToken cancellationToken)
    {
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var pending = await db.Set<PaymentOutboxEntry>()
            .Where(e => e.Status == "pending" && e.RetryCount < MaxRetries)
            .OrderBy(e => e.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var entry in pending)
        {
            var success = await _backoff.ExecuteWithRetryAsync(async () =>
            {
                await _producer.PublishAsync(entry.EventType, entry.Id.ToString(), entry.Payload, cancellationToken);
            }, cancellationToken: cancellationToken);

            if (success)
            {
                entry.Status = "processed";
                entry.ProcessedAt = DateTime.UtcNow;
            }
            else
            {
                entry.RetryCount++;
                if (entry.RetryCount >= MaxRetries)
                {
                    entry.Status = "dlq";
                    try
                    {
                        await _producer.PublishAsync(
                            $"{entry.EventType}-dlq",
                            entry.Id.ToString(),
                            entry.Payload,
                            cancellationToken);
                        _logger.LogError("Entry {Id} published to DLQ topic {Topic} after {Retries} retries",
                            entry.Id, $"{entry.EventType}-dlq", entry.RetryCount);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to publish entry {Id} to DLQ topic — entry marked dlq in DB only", entry.Id);
                    }
                }
                else
                {
                    entry.Status = "pending";
                    _logger.LogWarning("Entry {Id} retry {RetryCount}/{MaxRetries}", entry.Id, entry.RetryCount, MaxRetries);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
