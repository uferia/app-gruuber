using System.Text.Json;
using Gruuber.Payments.Domain;
using Gruuber.Payments.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Gruuber.Payments.Application.Commands;

public class ConfirmPaymentHandler
{
    private readonly PaymentsDbContext _db;
    private readonly ILogger<ConfirmPaymentHandler> _logger;

    public ConfirmPaymentHandler(PaymentsDbContext db, ILogger<ConfirmPaymentHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ApplicationResult<PaymentStatusResponse>> HandleAsync(
        ConfirmPaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        var payment = await _db.Payments.FindAsync(new object[] { command.PaymentId }, cancellationToken);
        if (payment is null)
            return ApplicationResult<PaymentStatusResponse>.Failure("PAYMENT_NOT_FOUND", "Payment not found.", 404);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var confirmed = payment.TryConfirm(command.ExpectedVersion);
        if (!confirmed)
            return ApplicationResult<PaymentStatusResponse>.Conflict(payment.Id, payment.Version);

        _db.Set<PaymentOutboxEntry>().Add(new PaymentOutboxEntry
        {
            EventType = $"payment-events-{command.RegionId}",
            Payload = JsonSerializer.Serialize(new
            {
                EventName = "payment_confirmed",
                PaymentId = payment.Id,
                payment.RideId,
                OccurredAt = DateTime.UtcNow
            })
        });

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation("Payment {PaymentId} confirmed for ride {RideId}", payment.Id, payment.RideId);
        return ApplicationResult<PaymentStatusResponse>.Success(new PaymentStatusResponse(payment.Id, payment.Status.ToString()));
    }
}

public class FailPaymentHandler
{
    private readonly PaymentsDbContext _db;
    private readonly ILogger<FailPaymentHandler> _logger;

    public FailPaymentHandler(PaymentsDbContext db, ILogger<FailPaymentHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ApplicationResult<PaymentStatusResponse>> HandleAsync(
        FailPaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        var payment = await _db.Payments.FindAsync(new object[] { command.PaymentId }, cancellationToken);
        if (payment is null)
            return ApplicationResult<PaymentStatusResponse>.Failure("PAYMENT_NOT_FOUND", "Payment not found.", 404);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var failed = payment.TryFail(command.ExpectedVersion);
        if (!failed)
            return ApplicationResult<PaymentStatusResponse>.Conflict(payment.Id, payment.Version);

        _db.Set<PaymentOutboxEntry>().Add(new PaymentOutboxEntry
        {
            EventType = $"payment-events-{command.RegionId}",
            Payload = JsonSerializer.Serialize(new
            {
                EventName = "payment_failed",
                PaymentId = payment.Id,
                payment.RideId,
                Reason = command.Reason,
                OccurredAt = DateTime.UtcNow
            })
        });

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogWarning("Payment {PaymentId} failed: {Reason}", payment.Id, command.Reason);
        return ApplicationResult<PaymentStatusResponse>.Success(new PaymentStatusResponse(payment.Id, payment.Status.ToString()));
    }
}

public record PaymentStatusResponse(Guid PaymentId, string Status);
