using FluentAssertions;
using Gruuber.Orders.Domain;
using Gruuber.SharedKernel.Payments;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class OrderDomainTests
{
    private static Order NewDeliveryOrder(PaymentMethod method = PaymentMethod.CardMock) =>
        Order.CreateForDelivery(Guid.NewGuid(), Guid.NewGuid(), 1, 14.60, 120.98, method);

    [TestMethod]
    public void CreateForDelivery_StartsPlacedNoRideVersion1()
    {
        var order = NewDeliveryOrder(PaymentMethod.CashOnDelivery);

        order.Status.Should().Be(OrderStatus.Placed);
        order.RideId.Should().BeNull();
        order.DriverId.Should().BeNull();
        order.PaymentMethod.Should().Be(PaymentMethod.CashOnDelivery);
        order.DeliveryLat.Should().Be(14.60);
        order.DeliveryLng.Should().Be(120.98);
        order.Version.Should().Be(1);
        order.CancellationReason.Should().BeNull();
    }

    [TestMethod]
    public void SetDeliveryFee_StoresFee()
    {
        var order = NewDeliveryOrder();

        order.SetDeliveryFee(2.50m);

        order.DeliveryFee.Should().Be(2.50m);
    }

    [TestMethod]
    public void TryCancel_WithCorrectVersion_SetsCancellationFieldsAndBumpsVersion()
    {
        var order = NewDeliveryOrder();

        var ok = order.TryCancel(OrderCancellationReason.TooBusy, "Kitchen slammed", "restaurant", 1);

        ok.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be(OrderCancellationReason.TooBusy);
        order.CancellationNote.Should().Be("Kitchen slammed");
        order.CancelledByRole.Should().Be("restaurant");
        order.Version.Should().Be(2);
    }

    [TestMethod]
    public void TryCancel_WithStaleVersion_ReturnsFalseAndChangesNothing()
    {
        var order = NewDeliveryOrder();

        var ok = order.TryCancel(OrderCancellationReason.TooBusy, null, "restaurant", 99);

        ok.Should().BeFalse();
        order.Status.Should().Be(OrderStatus.Placed);
        order.CancellationReason.Should().BeNull();
        order.Version.Should().Be(1);
    }

    [TestMethod]
    public void CancellationPolicy_RestaurantReasons()
    {
        CancellationPolicy.IsAllowed(OrderCancellationReason.ItemUnavailable, "restaurant").Should().BeTrue();
        CancellationPolicy.IsAllowed(OrderCancellationReason.TechnicalIssue, "restaurant").Should().BeTrue();
        CancellationPolicy.IsAllowed(OrderCancellationReason.OrderedByMistake, "restaurant").Should().BeFalse();
        CancellationPolicy.IsAllowed(OrderCancellationReason.NoDriverAvailable, "restaurant").Should().BeFalse();
    }

    [TestMethod]
    public void CancellationPolicy_CustomerReasons()
    {
        CancellationPolicy.IsAllowed(OrderCancellationReason.OrderedByMistake, "rider").Should().BeTrue();
        CancellationPolicy.IsAllowed(OrderCancellationReason.DuplicateOrder, "rider").Should().BeTrue();
        CancellationPolicy.IsAllowed(OrderCancellationReason.TooBusy, "rider").Should().BeFalse();
        CancellationPolicy.IsAllowed(OrderCancellationReason.PaymentFailed, "rider").Should().BeFalse();
    }

    [TestMethod]
    public void CancellationPolicy_SystemAndAdmin()
    {
        CancellationPolicy.IsAllowed(OrderCancellationReason.RestaurantUnresponsive, "system").Should().BeTrue();
        CancellationPolicy.IsAllowed(OrderCancellationReason.NoDriverAvailable, "system").Should().BeTrue();
        CancellationPolicy.IsAllowed(OrderCancellationReason.PaymentFailed, "system").Should().BeTrue();
        CancellationPolicy.IsAllowed(OrderCancellationReason.ItemUnavailable, "system").Should().BeFalse();
        CancellationPolicy.IsAllowed(OrderCancellationReason.ItemUnavailable, "admin").Should().BeTrue();
        CancellationPolicy.IsAllowed(OrderCancellationReason.NoDriverAvailable, "admin").Should().BeTrue();
        CancellationPolicy.IsAllowed(OrderCancellationReason.ItemUnavailable, "driver").Should().BeFalse();
    }
}
