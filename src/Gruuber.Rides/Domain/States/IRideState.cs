namespace Gruuber.Rides.Domain.States;

/// <summary>
/// State pattern — encapsulates ride-status-specific behaviour so that
/// valid transitions are enforced by the state object, not by scattered if/switch logic.
/// </summary>
public interface IRideState
{
    RideStatus Status { get; }

    /// <summary>Returns the set of statuses this state can legally transition to.</summary>
    IReadOnlySet<RideStatus> AllowedTransitions { get; }

    /// <summary>Validates the requested transition and throws if illegal.</summary>
    void ValidateTransition(RideStatus next);
}
