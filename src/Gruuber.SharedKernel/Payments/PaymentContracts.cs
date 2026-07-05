namespace Gruuber.SharedKernel.Payments;

public enum PaymentMethod
{
    CardMock,
    CashOnDelivery
}

public record OrderPaymentRequest(
    Guid OrderId,
    Guid RiderId,
    decimal Amount,
    string Currency,
    PaymentMethod Method,
    int RegionId);

public record OrderPaymentResult(Guid PaymentId, string Status);

public interface IOrderPaymentInitiator
{
    /// <summary>
    /// Creates a payment record for an order at placement time.
    /// Throws on persistence failure — callers treat any exception as payment-initiation failure.
    /// </summary>
    Task<OrderPaymentResult> InitiateForOrderAsync(OrderPaymentRequest request, CancellationToken cancellationToken = default);
}
