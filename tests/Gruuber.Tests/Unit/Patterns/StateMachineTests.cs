using Gruuber.Orders.Domain;
using Gruuber.Orders.Domain.States;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Domain.States;
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
        Assert.IsInstanceOfType(RideStateFactory.For(RideStatus.Requested),      typeof(RequestedState));
        Assert.IsInstanceOfType(RideStateFactory.For(RideStatus.PoolQueued),     typeof(PoolQueuedState));
        Assert.IsInstanceOfType(RideStateFactory.For(RideStatus.PoolMatched),    typeof(PoolMatchedState));
        Assert.IsInstanceOfType(RideStateFactory.For(RideStatus.Matched),        typeof(MatchedState));
        Assert.IsInstanceOfType(RideStateFactory.For(RideStatus.EnRoute),        typeof(EnRouteState));
        Assert.IsInstanceOfType(RideStateFactory.For(RideStatus.PartialDropoff), typeof(PartialDropoffState));
        Assert.IsInstanceOfType(RideStateFactory.For(RideStatus.Arrived),        typeof(ArrivedState));
        Assert.IsInstanceOfType(RideStateFactory.For(RideStatus.Completed),      typeof(CompletedState));
        Assert.IsInstanceOfType(RideStateFactory.For(RideStatus.Cancelled),      typeof(CancelledState));
    }

    [TestMethod]
    public void RequestedState_AllowsMatchedAndCancelled()
    {
        // Arrange
        var state = new RequestedState();

        // Assert
        Assert.IsTrue(state.AllowedTransitions.Contains(RideStatus.Matched));
        Assert.IsTrue(state.AllowedTransitions.Contains(RideStatus.Cancelled));
        Assert.IsFalse(state.AllowedTransitions.Contains(RideStatus.Completed));
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
        Assert.IsTrue(state.AllowedTransitions.Contains(RideStatus.Arrived));
        Assert.IsTrue(state.AllowedTransitions.Contains(RideStatus.PartialDropoff));
        Assert.IsTrue(state.AllowedTransitions.Contains(RideStatus.Cancelled));
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
        Assert.AreEqual(0, state.AllowedTransitions.Count);
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
        Assert.AreEqual(0, new CancelledState().AllowedTransitions.Count);
    }

    [TestMethod]
    public void PoolQueuedState_AllowsPoolMatchedRequestedAndCancelled()
    {
        // Arrange
        var state = new PoolQueuedState();

        // Assert
        Assert.IsTrue(state.AllowedTransitions.Contains(RideStatus.PoolMatched));
        Assert.IsTrue(state.AllowedTransitions.Contains(RideStatus.Requested)); // solo upgrade
        Assert.IsTrue(state.AllowedTransitions.Contains(RideStatus.Cancelled));
    }

    [TestMethod]
    public void ArrivedState_OnlyAllowsCompleted()
    {
        // Arrange
        var state = new ArrivedState();

        // Assert
        Assert.AreEqual(1, state.AllowedTransitions.Count);
        Assert.IsTrue(state.AllowedTransitions.Contains(RideStatus.Completed));
    }

    [TestMethod]
    public void RideStateFactory_Status_MatchesConstructedState()
    {
        // Verify Status property round-trips correctly for each enum value
        foreach (RideStatus status in Enum.GetValues<RideStatus>())
        {
            var state = RideStateFactory.For(status);
            Assert.AreEqual(status, state.Status, $"Status mismatch for {status}");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Order State Machine
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void OrderStateFactory_For_ReturnsCorrectStateType()
    {
        Assert.IsInstanceOfType(OrderStateFactory.For(OrderStatus.Placed),     typeof(PlacedState));
        Assert.IsInstanceOfType(OrderStateFactory.For(OrderStatus.Accepted),   typeof(AcceptedState));
        Assert.IsInstanceOfType(OrderStateFactory.For(OrderStatus.Preparing),  typeof(PreparingState));
        Assert.IsInstanceOfType(OrderStateFactory.For(OrderStatus.Ready),      typeof(ReadyState));
        Assert.IsInstanceOfType(OrderStateFactory.For(OrderStatus.PickedUp),   typeof(PickedUpState));
        Assert.IsInstanceOfType(OrderStateFactory.For(OrderStatus.Delivered),  typeof(DeliveredState));
        Assert.IsInstanceOfType(OrderStateFactory.For(OrderStatus.Cancelled),  typeof(OrderCancelledState));
    }

    [TestMethod]
    public void PlacedState_AllowsAcceptedAndCancelled()
    {
        // Arrange
        var state = new PlacedState();

        // Assert
        Assert.IsTrue(state.AllowedTransitions.Contains(OrderStatus.Accepted));
        Assert.IsTrue(state.AllowedTransitions.Contains(OrderStatus.Cancelled));
        Assert.IsFalse(state.AllowedTransitions.Contains(OrderStatus.Delivered));
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
        Assert.IsTrue(state.AllowedTransitions.Contains(OrderStatus.Ready));
        Assert.IsTrue(state.AllowedTransitions.Contains(OrderStatus.Cancelled));
    }

    [TestMethod]
    public void DeliveredState_IsTerminal_HasNoAllowedTransitions()
    {
        Assert.AreEqual(0, new DeliveredState().AllowedTransitions.Count);
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
        Assert.AreEqual(1, state.AllowedTransitions.Count);
        Assert.IsTrue(state.AllowedTransitions.Contains(OrderStatus.PickedUp));
    }

    [TestMethod]
    public void OrderStateFactory_Status_MatchesConstructedState()
    {
        foreach (OrderStatus status in Enum.GetValues<OrderStatus>())
        {
            var state = OrderStateFactory.For(status);
            Assert.AreEqual(status, state.Status, $"Status mismatch for {status}");
        }
    }
}
