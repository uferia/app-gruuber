using System.Text.Json;
using FluentAssertions;
using Gruuber.Payments.Application;
using Gruuber.Payments.Domain;
using Gruuber.Payments.Infrastructure;
using Gruuber.SharedKernel.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class OrderPaymentInitiatorTests
{
    private static PaymentsDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new PaymentsDbContext(opts);
    }

    [TestMethod]
    public async Task Initiate_CreatesPaymentWithOrderIdAndMethod()
    {
        await using var db = CreateInMemoryDb();
        var initiator = new OrderPaymentInitiator(db, NullLogger<OrderPaymentInitiator>.Instance);
        var orderId = Guid.NewGuid();
        var riderId = Guid.NewGuid();

        var result = await initiator.InitiateForOrderAsync(
            new OrderPaymentRequest(orderId, riderId, 392.50m, "PHP", PaymentMethod.CashOnDelivery, 1));

        result.Status.Should().Be("Initiated");
        var payment = await db.Payments.SingleAsync();
        payment.Id.Should().Be(result.PaymentId);
        payment.OrderId.Should().Be(orderId);
        payment.RiderId.Should().Be(riderId);
        payment.RideId.Should().BeNull();
        payment.Method.Should().Be(PaymentMethod.CashOnDelivery);
        payment.Amount.Should().Be(392.50m);
        payment.Status.Should().Be(PaymentStatus.Initiated);
        payment.RegionId.Should().Be(1);
    }

    [TestMethod]
    public async Task Initiate_WritesPaymentInitiatedOutboxEvent()
    {
        await using var db = CreateInMemoryDb();
        var initiator = new OrderPaymentInitiator(db, NullLogger<OrderPaymentInitiator>.Instance);
        var orderId = Guid.NewGuid();

        await initiator.InitiateForOrderAsync(
            new OrderPaymentRequest(orderId, Guid.NewGuid(), 100m, "PHP", PaymentMethod.CardMock, 7));

        var outbox = await db.Set<PaymentOutboxEntry>().SingleAsync();
        outbox.EventType.Should().Be("payment-events-7");
        using var doc = JsonDocument.Parse(outbox.Payload);
        doc.RootElement.GetProperty("EventName").GetString().Should().Be("payment_initiated");
        doc.RootElement.GetProperty("OrderId").GetGuid().Should().Be(orderId);
        doc.RootElement.GetProperty("Method").GetString().Should().Be("CardMock");
    }

    [TestMethod]
    public void CreateForOrder_DefaultsInitiatedVersion1()
    {
        var payment = Payment.CreateForOrder(Guid.NewGuid(), Guid.NewGuid(), 50m, "USD", PaymentMethod.CardMock, 3);

        payment.Status.Should().Be(PaymentStatus.Initiated);
        payment.Version.Should().Be(1);
        payment.RideId.Should().BeNull();
        payment.OrderId.Should().NotBeNull();
        payment.RegionId.Should().Be(3);
    }

    [TestMethod]
    public void Create_ForRide_StillWorks_MethodDefaultsCardMock()
    {
        var rideId = Guid.NewGuid();
        var payment = Payment.Create(rideId, Guid.NewGuid(), 25m, "USD");

        payment.RideId.Should().Be(rideId);
        payment.OrderId.Should().BeNull();
        payment.Method.Should().Be(PaymentMethod.CardMock);
    }
}
