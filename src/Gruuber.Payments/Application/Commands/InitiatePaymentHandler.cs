using System.Text.Json;
using Gruuber.Payments.Domain;
using Gruuber.Payments.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gruuber.Payments.Application.Commands;

public class InitiatePaymentHandler
{
    private readonly PaymentsDbContext _db;
    private readonly ILogger<InitiatePaymentHandler> _logger;

    public InitiatePaymentHandler(PaymentsDbContext db, ILogger<InitiatePaymentHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ApplicationResult<InitiatePaymentResponse>> HandleAsync(
        InitiatePaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        var payment = Payment.Create(command.RideId, command.RiderId, command.Amount, command.Currency);

        var outbox = new PaymentOutboxEntry
        {
            EventType = $"payment-events-{command.RegionId}",
            Payload = JsonSerializer.Serialize(new
            {
                EventName = "payment_initiated",
                PaymentId = payment.Id,
                payment.RideId,
                payment.RiderId,
                payment.Amount,
                payment.Currency,
                OccurredAt = DateTime.UtcNow
            })
        };

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.Payments.Add(payment);
        _db.Set<PaymentOutboxEntry>().Add(outbox);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation("Payment {PaymentId} initiated for ride {RideId} amount {Amount} {Currency}",
            payment.Id, payment.RideId, payment.Amount, payment.Currency);

        return ApplicationResult<InitiatePaymentResponse>.Accepted(
            new InitiatePaymentResponse(payment.Id, payment.Status.ToString()));
    }
}
