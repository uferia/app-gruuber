namespace Gruuber.Rides.Domain.States;

/// <summary>
/// Abstract base providing shared transition-guard logic for all ride states.
/// </summary>
public abstract class RideStateBase(RideStatus status, params RideStatus[] allowed) : IRideState
{
    public RideStatus Status { get; } = status;
    public IReadOnlySet<RideStatus> AllowedTransitions { get; } = new HashSet<RideStatus>(allowed);

    public void ValidateTransition(RideStatus next)
    {
        if (!AllowedTransitions.Contains(next))
            throw new InvalidOperationException(
                $"Cannot transition ride from {Status} to {next}. " +
                $"Allowed: [{string.Join(", ", AllowedTransitions)}].");
    }
}

// ── Concrete states ───────────────────────────────────────────────────────────

public sealed class RequestedState()
    : RideStateBase(RideStatus.Requested, RideStatus.Matched, RideStatus.Cancelled);

public sealed class PoolQueuedState()
    : RideStateBase(RideStatus.PoolQueued, RideStatus.PoolMatched, RideStatus.Requested, RideStatus.Cancelled);

public sealed class PoolMatchedState()
    : RideStateBase(RideStatus.PoolMatched, RideStatus.Matched, RideStatus.Cancelled);

public sealed class MatchedState()
    : RideStateBase(RideStatus.Matched, RideStatus.EnRoute, RideStatus.Cancelled);

public sealed class EnRouteState()
    : RideStateBase(RideStatus.EnRoute, RideStatus.Arrived, RideStatus.PartialDropoff, RideStatus.Cancelled);

public sealed class PartialDropoffState()
    : RideStateBase(RideStatus.PartialDropoff, RideStatus.Arrived, RideStatus.Cancelled);

public sealed class ArrivedState()
    : RideStateBase(RideStatus.Arrived, RideStatus.Completed);

public sealed class CompletedState()
    : RideStateBase(RideStatus.Completed);

public sealed class CancelledState()
    : RideStateBase(RideStatus.Cancelled);

// ── Factory ───────────────────────────────────────────────────────────────────

public static class RideStateFactory
{
    public static IRideState For(RideStatus status) => status switch
    {
        RideStatus.Requested      => new RequestedState(),
        RideStatus.PoolQueued     => new PoolQueuedState(),
        RideStatus.PoolMatched    => new PoolMatchedState(),
        RideStatus.Matched        => new MatchedState(),
        RideStatus.EnRoute        => new EnRouteState(),
        RideStatus.PartialDropoff => new PartialDropoffState(),
        RideStatus.Arrived        => new ArrivedState(),
        RideStatus.Completed      => new CompletedState(),
        RideStatus.Cancelled      => new CancelledState(),
        _                         => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}
