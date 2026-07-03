using Gruuber.Orders.Domain;
using Gruuber.Orders.Domain.States;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Domain.States;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gruuber.Tests.Unit.Patterns;

/// <summary>
/// Tests for the formal State pattern on Ride and Order aggregates.
/// Verifies that state objects correctly report allowed transitions and
/// throw on illegal ones.
/// </summary>
[TestClass]
public class StateMachineTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // Ride State Machine
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void RideStateFactory_For_ReturnsCorrectStateType()
    {
        // Arrange / Act / Assert — each status maps to its concrete state class
        RideStateFactory.For(RideStatus.Requested).Should().BeOfType<RequestedState>();
        RideStateFactory.For(RideStatus.PoolQueued).Should().BeOfType<PoolQueuedState>();
        RideStateFactory.For(RideStatus.PoolMatched).Should().BeOfType<PoolMatchedState>();
        RideStateFactory.For(RideStatus.Matched).Should().BeOfType<MatchedState>();
        RideStateFactory.For(RideStatus.EnRoute).Should().BeOfType<EnRouteState>();
        RideStateFactory.For(RideStatus.PartialDropoff).Should().BeOfType<PartialDropoffState>();
        RideStateFactory.For(RideStatus.Arrived).Should().BeOfType<ArrivedState>();
        RideStateFactory.For(RideStatus.Completed).Should().BeOfType<CompletedState>();
        RideStateFactory.For(RideStatus.Cancelled).Should().BeOfType<CancelledState>();
    }

    [TestMethod]
    public void RequestedState_AllowsMatchedAndCancelled()
    {
        // Arrange
        var state = new RequestedState();

        // Assert
        state.AllowedTransitions.Contains(RideStatus.Matched).Should().BeTrue();
        state.AllowedTransitions.Contains(RideStatus.Cancelled).Should().BeTrue();
        state.AllowedTransitions.Contains(RideStatus.Completed).Should().BeFalse();
    }

    [TestMethod]
    public void RequestedState_ValidateTransition_ToMatched_DoesNotThrow()
    {
        // Arrange
        var state = new RequestedState();

        // Act / Assert — no exception expected
        state.ValidateTransition(RideStatus.Matched);
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidOperationException))]
    public void RequestedState_ValidateTransition_ToCompleted_Throws()
    {
        // Arrange
        var state = new RequestedState();

        // Act — illegal transition
        state.ValidateTransition(RideStatus.Completed);
    }

    [TestMethod]
    public void EnRouteState_AllowsPartialDropoffArrivedAndCancelled()
    {
        // Arrange
        var state = new EnRouteState();

        // Assert
        state.AllowedTransitions.Contains(RideStatus.Arrived).Should().BeTrue();
        state.AllowedTransitions.Contains(RideStatus.PartialDropoff).Should().BeTrue();
        state.AllowedTransitions.Contains(RideStatus.Cancelled).Should().BeTrue();
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidOperationException))]
    public void EnRouteState_ValidateTransition_ToRequested_Throws()
    {
        new EnRouteState().ValidateTransition(RideStatus.Requested);
    }

    [TestMethod]
    public void CompletedState_HasNoAllowedTransitions()
    {
        // Arrange
        var state = new CompletedState();

        // Assert — terminal state
        state.AllowedTransitions.Count.Should().Be(0);
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidOperationException))]
    public void CompletedState_ValidateTransition_ToAnything_Throws()
    {
        new CompletedState().ValidateTransition(RideStatus.Cancelled);
    }

    [TestMethod]
    public void CancelledState_HasNoAllowedTransitions()
    {
        // Assert — terminal state
        new CancelledState().AllowedTransitions.Count.Should().Be(0);
    }

    [TestMethod]
    public void PoolQueuedState_AllowsPoolMatchedRequestedAndCancelled()
    {
        // Arrange
        var state = new PoolQueuedState();

        // Assert
        state.AllowedTransitions.Contains(RideStatus.PoolMatched).Should().BeTrue();
        state.AllowedTransitions.Contains(RideStatus.Requested).Should().BeTrue(); // solo upgrade
        state.AllowedTransitions.Contains(RideStatus.Cancelled).Should().BeTrue();
    }

    [TestMethod]
    public void ArrivedState_OnlyAllowsCompleted()
    {
        // Arrange
        var state = new ArrivedState();

        // Assert
        state.AllowedTransitions.Count.Should().Be(1);
        state.AllowedTransitions.Contains(RideStatus.Completed).Should().BeTrue();
    }

    [TestMethod]
    public void RideStateFactory_Status_MatchesConstructedState()
    {
        // Verify Status property round-trips correctly for each enum value
        foreach (RideStatus status in Enum.GetValues<RideStatus>())
        {
            var state = RideStateFactory.For(status);
            state.Status.Should().Be(status, $"Status mismatch for {status}");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Order State Machine
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void OrderStateFactory_For_ReturnsCorrectStateType()
    {
        OrderStateFactory.For(OrderStatus.Placed).Should().BeOfType<PlacedState>();
        OrderStateFactory.For(OrderStatus.Accepted).Should().BeOfType<AcceptedState>();
        OrderStateFactory.For(OrderStatus.Preparing).Should().BeOfType<PreparingState>();
        OrderStateFactory.For(OrderStatus.Ready).Should().BeOfType<ReadyState>();
        OrderStateFactory.For(OrderStatus.PickedUp).Should().BeOfType<PickedUpState>();
        OrderStateFactory.For(OrderStatus.Delivered).Should().BeOfType<DeliveredState>();
        OrderStateFactory.For(OrderStatus.Cancelled).Should().BeOfType<OrderCancelledState>();
    }

    [TestMethod]
    public void PlacedState_AllowsAcceptedAndCancelled()
    {
        // Arrange
        var state = new PlacedState();

        // Assert
        state.AllowedTransitions.Contains(OrderStatus.Accepted).Should().BeTrue();
        state.AllowedTransitions.Contains(OrderStatus.Cancelled).Should().BeTrue();
        state.AllowedTransitions.Contains(OrderStatus.Delivered).Should().BeFalse();
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidOperationException))]
    public void PlacedState_ValidateTransition_ToDelivered_Throws()
    {
        new PlacedState().ValidateTransition(OrderStatus.Delivered);
    }

    [TestMethod]
    public void PreparingState_AllowsReadyAndCancelled()
    {
        var state = new PreparingState();
        state.AllowedTransitions.Contains(OrderStatus.Ready).Should().BeTrue();
        state.AllowedTransitions.Contains(OrderStatus.Cancelled).Should().BeTrue();
    }

    [TestMethod]
    public void DeliveredState_IsTerminal_HasNoAllowedTransitions()
    {
        new DeliveredState().AllowedTransitions.Count.Should().Be(0);
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidOperationException))]
    public void DeliveredState_ValidateTransition_Throws()
    {
        new DeliveredState().ValidateTransition(OrderStatus.Cancelled);
    }

    [TestMethod]
    public void ReadyState_OnlyAllowsPickedUp()
    {
        var state = new ReadyState();
        state.AllowedTransitions.Count.Should().Be(1);
        state.AllowedTransitions.Contains(OrderStatus.PickedUp).Should().BeTrue();
    }

    [TestMethod]
    public void OrderStateFactory_Status_MatchesConstructedState()
    {
        foreach (OrderStatus status in Enum.GetValues<OrderStatus>())
        {
            var state = OrderStateFactory.For(status);
            state.Status.Should().Be(status, $"Status mismatch for {status}");
        }
    }
}
