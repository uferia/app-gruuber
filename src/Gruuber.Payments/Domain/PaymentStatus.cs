namespace Gruuber.Payments.Domain;

public enum PaymentStatus
{
    Initiated,
    PendingConfirmation,
    Succeeded,
    Failed,
    FailedTimeout,
    Refunded
}
