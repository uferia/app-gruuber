using Gruuber.SharedKernel.Domain;

namespace Gruuber.Orders.Application.Specifications;

/// <summary>
/// Input data for order eligibility evaluation.
/// </summary>
public record OrderEligibilityContext(
    Guid RestaurantId,
    bool RestaurantIsOpen,
    int ItemCount,
    decimal TotalAmount,
    int RegionId);

/// <summary>
/// Specification — restaurant must be open to accept new orders.
/// </summary>
public sealed class RestaurantOpenSpecification : ISpecification<OrderEligibilityContext>
{
    public bool IsSatisfiedBy(OrderEligibilityContext ctx) =>
        ctx.RestaurantIsOpen;
}

/// <summary>
/// Specification — order must contain at least one item.
/// </summary>
public sealed class OrderHasItemsSpecification : ISpecification<OrderEligibilityContext>
{
    public bool IsSatisfiedBy(OrderEligibilityContext ctx) =>
        ctx.ItemCount > 0;
}

/// <summary>
/// Specification — order total must meet the minimum basket size.
/// </summary>
public sealed class MinimumOrderAmountSpecification(decimal minimumAmount = 1.0m)
    : ISpecification<OrderEligibilityContext>
{
    public bool IsSatisfiedBy(OrderEligibilityContext ctx) =>
        ctx.TotalAmount >= minimumAmount;
}

/// <summary>
/// Composite specification — an order is eligible when the restaurant is open,
/// the basket has items, and the total exceeds the minimum.
/// </summary>
public sealed class OrderEligibilitySpecification : ISpecification<OrderEligibilityContext>
{
    private readonly ISpecification<OrderEligibilityContext> _composite;

    public OrderEligibilitySpecification(decimal minimumOrderAmount = 1.0m)
    {
        _composite = new RestaurantOpenSpecification()
            .And(new OrderHasItemsSpecification())
            .And(new MinimumOrderAmountSpecification(minimumOrderAmount));
    }

    public bool IsSatisfiedBy(OrderEligibilityContext ctx) =>
        _composite.IsSatisfiedBy(ctx);
}
