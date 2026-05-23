using Gruuber.Payments.Infrastructure;
using Gruuber.SharedKernel.Results;

namespace Gruuber.Payments.Application.Commands;

public class GetPaymentHandler
{
    private readonly PaymentsDbContext _db;

    public GetPaymentHandler(PaymentsDbContext db) => _db = db;

    public async Task<ApplicationResult<PaymentDetailResponse>> HandleAsync(
        GetPaymentQuery query,
        CancellationToken cancellationToken = default)
    {
        var payment = await _db.Payments.FindAsync(new object[] { query.PaymentId }, cancellationToken);
        if (payment is null)
            return ApplicationResult<PaymentDetailResponse>.Failure("PAYMENT_NOT_FOUND", "Payment not found.", 404);

        return ApplicationResult<PaymentDetailResponse>.Success(
            new PaymentDetailResponse(
                payment.Id,
                payment.RideId,
                payment.Status.ToString(),
                payment.Amount,
                payment.Currency,
                payment.CreatedAt));
    }
}
