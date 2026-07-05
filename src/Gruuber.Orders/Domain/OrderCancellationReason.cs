namespace Gruuber.Orders.Domain;

public enum OrderCancellationReason
{
    // Restaurant reject reasons
    ItemUnavailable,
    TooBusy,
    ClosingSoon,
    MenuPriceIncorrect,
    CannotFulfillInstructions,
    TechnicalIssue,
    // Customer cancel reasons
    OrderedByMistake,
    WrongAddress,
    TakingTooLong,
    PaymentIssue,
    DuplicateOrder,
    // System reasons
    RestaurantUnresponsive,
    NoDriverAvailable,
    PaymentFailed
}

public static class CancellationPolicy
{
    private static readonly OrderCancellationReason[] RestaurantReasons =
    {
        OrderCancellationReason.ItemUnavailable,
        OrderCancellationReason.TooBusy,
        OrderCancellationReason.ClosingSoon,
        OrderCancellationReason.MenuPriceIncorrect,
        OrderCancellationReason.CannotFulfillInstructions,
        OrderCancellationReason.TechnicalIssue
    };

    private static readonly OrderCancellationReason[] CustomerReasons =
    {
        OrderCancellationReason.OrderedByMistake,
        OrderCancellationReason.WrongAddress,
        OrderCancellationReason.TakingTooLong,
        OrderCancellationReason.PaymentIssue,
        OrderCancellationReason.DuplicateOrder
    };

    private static readonly OrderCancellationReason[] SystemReasons =
    {
        OrderCancellationReason.RestaurantUnresponsive,
        OrderCancellationReason.NoDriverAvailable,
        OrderCancellationReason.PaymentFailed
    };

    public static bool IsAllowed(OrderCancellationReason reason, string actorRole) => actorRole switch
    {
        "restaurant" => RestaurantReasons.Contains(reason),
        "rider" => CustomerReasons.Contains(reason),
        "system" => SystemReasons.Contains(reason),
        "admin" => true,
        _ => false
    };
}
