namespace Gruuber.Orders.Domain.States;

/// <summary>
/// State pattern — encapsulates order-status-specific behaviour so that
/// valid transitions are enforced by the state object.
/// </summary>
public interface IOrderState
{
    OrderStatus Status { get; }
    IReadOnlySet<OrderStatus> AllowedTransitions { get; }
    void ValidateTransition(OrderStatus next);
}

public abstract class OrderStateBase(OrderStatus status, params OrderStatus[] allowed) : IOrderState
{
    public OrderStatus Status { get; } = status;
    public IReadOnlySet<OrderStatus> AllowedTransitions { get; } = new HashSet<OrderStatus>(allowed);

    public void ValidateTransition(OrderStatus next)
    {
        if (!AllowedTransitions.Contains(next))
            throw new InvalidOperationException(
                $"Cannot transition order from {Status} to {next}. " +
                $"Allowed: [{string.Join(", ", AllowedTransitions)}].");
    }
}

// ── Concrete states ───────────────────────────────────────────────────────────

public sealed class PlacedState()
    : OrderStateBase(OrderStatus.Placed, OrderStatus.Accepted, OrderStatus.Cancelled);

public sealed class AcceptedState()
    : OrderStateBase(OrderStatus.Accepted, OrderStatus.Preparing, OrderStatus.Cancelled);

public sealed class PreparingState()
    : OrderStateBase(OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.Cancelled);

public sealed class ReadyState()
    : OrderStateBase(OrderStatus.Ready, OrderStatus.PickedUp);

public sealed class PickedUpState()
    : OrderStateBase(OrderStatus.PickedUp, OrderStatus.Delivered);

public sealed class DeliveredState()
    : OrderStateBase(OrderStatus.Delivered);

public sealed class OrderCancelledState()
    : OrderStateBase(OrderStatus.Cancelled);

// ── Factory ───────────────────────────────────────────────────────────────────

public static class OrderStateFactory
{
    public static IOrderState For(OrderStatus status) => status switch
    {
        OrderStatus.Placed     => new PlacedState(),
        OrderStatus.Accepted   => new AcceptedState(),
        OrderStatus.Preparing  => new PreparingState(),
        OrderStatus.Ready      => new ReadyState(),
        OrderStatus.PickedUp   => new PickedUpState(),
        OrderStatus.Delivered  => new DeliveredState(),
        OrderStatus.Cancelled  => new OrderCancelledState(),
        _                      => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}
