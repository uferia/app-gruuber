using System.Text.Json;
using FluentAssertions;
using Gruuber.Orders.Application;
using Gruuber.Orders.Application.Commands;
using Gruuber.Orders.Domain;
using Gruuber.Orders.Infrastructure;
using Gruuber.SharedKernel.Catalog;
using Gruuber.SharedKernel.Payments;
using Gruuber.SharedKernel.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class CreateOrderHandlerTests
{
    private static readonly Guid RestaurantId = Guid.NewGuid();
    private static readonly Guid ItemA = Guid.NewGuid();
    private static readonly Guid ItemB = Guid.NewGuid();

    private static OrdersDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new OrdersDbContext(opts);
    }

    private static CatalogRestaurant OpenRestaurant(int regionId = 1) =>
        new(RestaurantId, Guid.NewGuid(), "Kanto Grill", IsApproved: true, IsOpen: true, regionId, 14.5, 120.9);

    private static Mock<IRestaurantCatalogReader> CatalogWith(CatalogRestaurant? restaurant, params CatalogMenuItem[] items)
    {
        var catalog = new Mock<IRestaurantCatalogReader>();
        catalog.Setup(c => c.GetRestaurantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(restaurant);
        catalog.Setup(c => c.GetMenuItemsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(items.ToList());
        return catalog;
    }

    private static Mock<ISurgePricingService> NoSurge()
    {
        var surge = new Mock<ISurgePricingService>();
        surge.Setup(s => s.ResolveAsync(It.IsAny<int>(), "food", It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, string _, decimal baseFare, CancellationToken _) =>
                new SurgeResolution(1.0m, null, baseFare, baseFare));
        return surge;
    }

    private static Mock<IOrderPaymentInitiator> PaymentsOk()
    {
        var payments = new Mock<IOrderPaymentInitiator>();
        payments.Setup(p => p.InitiateForOrderAsync(It.IsAny<OrderPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderPaymentResult(Guid.NewGuid(), "Initiated"));
        return payments;
    }

    private static CreateOrderHandler Handler(
        OrdersDbContext db,
        Mock<IRestaurantCatalogReader> catalog,
        Mock<IOrderPaymentInitiator>? payments = null) =>
        new(db, catalog.Object, NoSurge().Object, (payments ?? PaymentsOk()).Object,
            new OrderPricingOptions(2.50m), NullLogger<CreateOrderHandler>.Instance);

    private static CreateOrderCommand Command(params OrderItemRequest[] items) =>
        new(Guid.NewGuid(), RestaurantId, 1, items.ToList(), 14.60, 120.98, PaymentMethod.CardMock);

    [TestMethod]
    public async Task Create_PricesFromMenuAndInitiatesPayment()
    {
        await using var db = CreateInMemoryDb();
        var catalog = CatalogWith(OpenRestaurant(),
            new CatalogMenuItem(ItemA, RestaurantId, "Pork BBQ", 120m, "PHP", true),
            new CatalogMenuItem(ItemB, RestaurantId, "Sisig", 150m, "PHP", true));
        var payments = PaymentsOk();
        var handler = Handler(db, catalog, payments);

        var result = await handler.HandleAsync(Command(new OrderItemRequest(ItemA, 2), new OrderItemRequest(ItemB, 1)));

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(202);
        // 2*120 + 1*150 = 390 subtotal; +2.50 delivery fee = 392.50; surge 1.0
        result.Data!.Total.Should().Be(392.50m);
        var order = await db.Orders.Include(o => o.Items).SingleAsync();
        order.TotalAmount.Should().Be(390m);
        order.DeliveryFee.Should().Be(2.50m);
        order.FinalFare.Should().Be(392.50m);
        order.RideId.Should().BeNull();
        order.Items.Should().HaveCount(2);
        order.Items.Single(i => i.MenuItemId == ItemA).Price.Should().Be(120m);
        payments.Verify(p => p.InitiateForOrderAsync(
            It.Is<OrderPaymentRequest>(r => r.OrderId == order.Id && r.Amount == 392.50m && r.Currency == "PHP" && r.Method == PaymentMethod.CardMock),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task Create_WritesOrderPlacedOutboxEvent()
    {
        await using var db = CreateInMemoryDb();
        var catalog = CatalogWith(OpenRestaurant(),
            new CatalogMenuItem(ItemA, RestaurantId, "Pork BBQ", 120m, "PHP", true));
        var handler = Handler(db, catalog);

        await handler.HandleAsync(Command(new OrderItemRequest(ItemA, 1)));

        var outbox = await db.Set<OrderOutboxEntry>().SingleAsync();
        outbox.EventType.Should().Be("order-events-1");
        using var doc = JsonDocument.Parse(outbox.Payload);
        doc.RootElement.GetProperty("EventName").GetString().Should().Be("order_placed");
    }

    [TestMethod]
    public async Task Create_UnknownRestaurant_Returns404()
    {
        await using var db = CreateInMemoryDb();
        var handler = Handler(db, CatalogWith(null));

        var result = await handler.HandleAsync(Command(new OrderItemRequest(ItemA, 1)));

        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("RESTAURANT_NOT_FOUND");
    }

    [TestMethod]
    public async Task Create_ClosedRestaurant_Returns400()
    {
        await using var db = CreateInMemoryDb();
        var closed = new CatalogRestaurant(RestaurantId, Guid.NewGuid(), "Kanto Grill", true, IsOpen: false, 1, 14.5, 120.9);
        var handler = Handler(db, CatalogWith(closed,
            new CatalogMenuItem(ItemA, RestaurantId, "Pork BBQ", 120m, "PHP", true)));

        var result = await handler.HandleAsync(Command(new OrderItemRequest(ItemA, 1)));

        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("RESTAURANT_UNAVAILABLE");
    }

    [TestMethod]
    public async Task Create_UnavailableItem_Returns400()
    {
        await using var db = CreateInMemoryDb();
        var handler = Handler(db, CatalogWith(OpenRestaurant(),
            new CatalogMenuItem(ItemA, RestaurantId, "Pork BBQ", 120m, "PHP", IsAvailable: false)));

        var result = await handler.HandleAsync(Command(new OrderItemRequest(ItemA, 1)));

        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("ITEM_UNAVAILABLE");
    }

    [TestMethod]
    public async Task Create_ItemFromAnotherRestaurant_Returns400()
    {
        await using var db = CreateInMemoryDb();
        var handler = Handler(db, CatalogWith(OpenRestaurant(),
            new CatalogMenuItem(ItemA, Guid.NewGuid(), "Foreign Item", 120m, "PHP", true)));

        var result = await handler.HandleAsync(Command(new OrderItemRequest(ItemA, 1)));

        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("INVALID_MENU_ITEM");
    }

    [TestMethod]
    public async Task Create_PaymentInitiationFails_CancelsOrderWithPaymentFailed()
    {
        await using var db = CreateInMemoryDb();
        var catalog = CatalogWith(OpenRestaurant(),
            new CatalogMenuItem(ItemA, RestaurantId, "Pork BBQ", 120m, "PHP", true));
        var payments = new Mock<IOrderPaymentInitiator>();
        payments.Setup(p => p.InitiateForOrderAsync(It.IsAny<OrderPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("payments store down"));
        var handler = Handler(db, catalog, payments);

        var result = await handler.HandleAsync(Command(new OrderItemRequest(ItemA, 1)));

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("PAYMENT_FAILED");
        var order = await db.Orders.SingleAsync();
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be(OrderCancellationReason.PaymentFailed);
        order.CancelledByRole.Should().Be("system");
    }
}
