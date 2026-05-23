namespace Gruuber.Payments.Application.Commands;

public record InitiatePaymentCommand(Guid RideId, Guid RiderId, decimal Amount, string Currency, int RegionId);
public record InitiatePaymentResponse(Guid PaymentId, string Status);

public record ConfirmPaymentCommand(Guid PaymentId, long ExpectedVersion, int RegionId);
public record FailPaymentCommand(Guid PaymentId, long ExpectedVersion, string Reason, int RegionId);

public record GetPaymentQuery(Guid PaymentId);
public record PaymentDetailResponse(Guid Id, Guid RideId, string Status, decimal Amount, string Currency, DateTime CreatedAt);
