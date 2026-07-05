using FluentAssertions;
using Gruuber.Orders.Application.Queries;
using Gruuber.Orders.Domain;
using Gruuber.Orders.Infrastructure;
using Gruuber.SharedKernel.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class GetRestaurantOrdersHandlerTests
{
    private static OrdersDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new OrdersDbContext(opts);
    }

    private static Order NewOrder(Guid restaurantId)
    {
        var order = Order.CreateForDelivery(Guid.NewGuid(), restaurantId, 1, 14.6, 120.98, PaymentMethod.CardMock);
        order.AddItem(Guid.NewGuid(), 1, 100m);
        return order;
    }

    [TestMethod]
    public async Task List_ReturnsOnlyThisRestaurantsOrders_NewestFirst()
    {
        await using var db = CreateInMemoryDb();
        var restaurantId = Guid.NewGuid();
        var mine = NewOrder(restaurantId);
        var other = NewOrder(Guid.NewGuid());
        db.Orders.AddRange(mine, other);
        await db.SaveChangesAsync();
        var handler = new GetRestaurantOrdersHandler(db);

        var result = await handler.HandleAsync(new GetRestaurantOrdersQuery(restaurantId, null, 1, 20));

        result.IsSuccess.Should().BeTrue();
        result.Data!.TotalCount.Should().Be(1);
        result.Data.Items.Single().OrderId.Should().Be(mine.Id);
        result.Data.Items.Single().Total.Should().Be(100m);
        result.Data.Items.Single().Version.Should().Be(1);
    }

    [TestMethod]
    public async Task List_FiltersByStatus()
    {
        await using var db = CreateInMemoryDb();
        var restaurantId = Guid.NewGuid();
        var placed = NewOrder(restaurantId);
        var accepted = NewOrder(restaurantId);
        accepted.TryTransition(OrderStatus.Accepted, 1);
        db.Orders.AddRange(placed, accepted);
        await db.SaveChangesAsync();
        var handler = new GetRestaurantOrdersHandler(db);

        var result = await handler.HandleAsync(new GetRestaurantOrdersQuery(restaurantId, "Placed", 1, 20));

        result.Data!.TotalCount.Should().Be(1);
        result.Data.Items.Single().OrderId.Should().Be(placed.Id);
        result.Data.Items.Single().Status.Should().Be("Placed");
    }

    [TestMethod]
    public async Task List_InvalidStatus_Returns400()
    {
        await using var db = CreateInMemoryDb();
        var handler = new GetRestaurantOrdersHandler(db);

        var result = await handler.HandleAsync(new GetRestaurantOrdersQuery(Guid.NewGuid(), "NotAStatus", 1, 20));

        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("INVALID_STATUS");
    }

    [TestMethod]
    public async Task List_Paginates()
    {
        await using var db = CreateInMemoryDb();
        var restaurantId = Guid.NewGuid();
        for (var i = 0; i < 25; i++)
            db.Orders.Add(NewOrder(restaurantId));
        await db.SaveChangesAsync();
        var handler = new GetRestaurantOrdersHandler(db);

        var page2 = await handler.HandleAsync(new GetRestaurantOrdersQuery(restaurantId, null, 2, 10));

        page2.Data!.TotalCount.Should().Be(25);
        page2.Data.Items.Should().HaveCount(10);
        page2.Data.Page.Should().Be(2);
    }
}
