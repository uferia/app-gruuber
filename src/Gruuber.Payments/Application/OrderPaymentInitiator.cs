using System.Text.Json;
using Gruuber.Payments.Domain;
using Gruuber.Payments.Infrastructure;
using Gruuber.SharedKernel.Payments;
using Microsoft.Extensions.Logging;

namespace Gruuber.Payments.Application;

public class OrderPaymentInitiator : IOrderPaymentInitiator
{
    private readonly PaymentsDbContext _db;
    private readonly ILogger<OrderPaymentInitiator> _logger;

    public OrderPaymentInitiator(PaymentsDbContext db, ILogger<OrderPaymentInitiator> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<OrderPaymentResult> InitiateForOrderAsync(
        OrderPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        var payment = Payment.CreateForOrder(request.OrderId, request.RiderId, request.Amount, request.Currency, request.Method);

        var outbox = new PaymentOutboxEntry
        {
            EventType = $"payment-events-{request.RegionId}",
            Payload = JsonSerializer.Serialize(new
            {
                EventName = "payment_initiated",
                PaymentId = payment.Id,
                payment.OrderId,
                payment.RiderId,
                payment.Amount,
                payment.Currency,
                Method = payment.Method.ToString(),
                OccurredAt = DateTime.UtcNow
            })
        };

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.Payments.Add(payment);
        _db.Set<PaymentOutboxEntry>().Add(outbox);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Payment {PaymentId} initiated for order {OrderId} amount {Amount} {Currency} method {Method}",
            payment.Id, payment.OrderId, payment.Amount, payment.Currency, payment.Method);

        return new OrderPaymentResult(payment.Id, payment.Status.ToString());
    }
}
