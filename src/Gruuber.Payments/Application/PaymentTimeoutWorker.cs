using System.Text.Json;
using Gruuber.Payments.Domain;
using Gruuber.Payments.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gruuber.Payments.Application;

/// <summary>
/// Polls for payments stuck in PendingConfirmation for > 15 minutes and triggers timeout/refund.
/// </summary>
public class PaymentTimeoutWorker : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TimeoutBound = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentTimeoutWorker> _logger;

    public PaymentTimeoutWorker(IServiceScopeFactory scopeFactory, ILogger<PaymentTimeoutWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PaymentTimeoutWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessTimedOutPaymentsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in PaymentTimeoutWorker");
            }

            await Task.Delay(PollingInterval, stoppingToken);
        }
    }

    private async Task ProcessTimedOutPaymentsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();

        var cutoff = DateTime.UtcNow - TimeoutBound;

        var timedOut = await db.Payments
            .Where(p => p.Status == PaymentStatus.PendingConfirmation && p.CreatedAt < cutoff)
            .ToListAsync(cancellationToken);

        foreach (var payment in timedOut)
        {
            var succeeded = payment.TryTimeout(payment.Version);
            if (!succeeded)
            {
                _logger.LogWarning("Version conflict timing out payment {PaymentId}", payment.Id);
                continue;
            }

            db.Set<PaymentOutboxEntry>().Add(new PaymentOutboxEntry
            {
                EventType = $"payment-events-timeout",
                Payload = JsonSerializer.Serialize(new
                {
                    EventName = "payment_timeout",
                    PaymentId = payment.Id,
                    payment.RideId,
                    OccurredAt = DateTime.UtcNow
                })
            });

            _logger.LogWarning("Payment {PaymentId} timed out for ride {RideId}", payment.Id, payment.RideId);
        }

        if (timedOut.Any())
            await db.SaveChangesAsync(cancellationToken);
    }
}
