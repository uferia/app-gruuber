using FluentAssertions;
using Gruuber.Restaurants.Application.Commands;
using Gruuber.Restaurants.Application.Queries;
using Gruuber.Restaurants.Domain;
using Gruuber.Restaurants.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class RestaurantAdminHandlerTests
{
    private static RestaurantsDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<RestaurantsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new RestaurantsDbContext(opts);
    }

    private static Restaurant NewRestaurant(string name = "Kanto Grill") =>
        Restaurant.Create(Guid.NewGuid(), name, "d", "Filipino", "a", 14.5, 120.9, 1);

    [TestMethod]
    public async Task Approve_WithCorrectVersion_SetsApproved()
    {
        await using var db = CreateInMemoryDb();
        var restaurant = NewRestaurant();
        db.Restaurants.Add(restaurant);
        await db.SaveChangesAsync();
        var handler = new ApproveRestaurantHandler(db);

        var result = await handler.HandleAsync(new ApproveRestaurantCommand(restaurant.Id, 1));

        result.IsSuccess.Should().BeTrue();
        result.Data!.ApprovalStatus.Should().Be("Approved");
        (await db.Restaurants.SingleAsync()).ApprovalStatus.Should().Be(RestaurantApprovalStatus.Approved);
    }

    [TestMethod]
    public async Task Approve_WithStaleVersion_Returns409()
    {
        await using var db = CreateInMemoryDb();
        var restaurant = NewRestaurant();
        db.Restaurants.Add(restaurant);
        await db.SaveChangesAsync();
        var handler = new ApproveRestaurantHandler(db);

        var result = await handler.HandleAsync(new ApproveRestaurantCommand(restaurant.Id, 99));

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("RESOURCE_CONFLICTED");
    }

    [TestMethod]
    public async Task Approve_UnknownRestaurant_Returns404()
    {
        await using var db = CreateInMemoryDb();
        var handler = new ApproveRestaurantHandler(db);

        var result = await handler.HandleAsync(new ApproveRestaurantCommand(Guid.NewGuid(), 1));

        result.StatusCode.Should().Be(404);
    }

    [TestMethod]
    public async Task Reject_SetsReasonAndStatus()
    {
        await using var db = CreateInMemoryDb();
        var restaurant = NewRestaurant();
        db.Restaurants.Add(restaurant);
        await db.SaveChangesAsync();
        var handler = new RejectRestaurantHandler(db);

        var result = await handler.HandleAsync(new RejectRestaurantCommand(restaurant.Id, 1, "Incomplete documents"));

        result.IsSuccess.Should().BeTrue();
        result.Data!.ApprovalStatus.Should().Be("Rejected");
        result.Data.Reason.Should().Be("Incomplete documents");
    }

    [TestMethod]
    public async Task Reject_UnknownRestaurant_Returns404()
    {
        await using var db = CreateInMemoryDb();
        var handler = new RejectRestaurantHandler(db);

        var result = await handler.HandleAsync(new RejectRestaurantCommand(Guid.NewGuid(), 1, "Incomplete documents"));

        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    [TestMethod]
    public async Task Reject_WithStaleVersion_Returns409()
    {
        await using var db = CreateInMemoryDb();
        var restaurant = NewRestaurant();
        db.Restaurants.Add(restaurant);
        await db.SaveChangesAsync();
        var handler = new RejectRestaurantHandler(db);

        var result = await handler.HandleAsync(new RejectRestaurantCommand(restaurant.Id, 99, "Incomplete documents"));

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("RESOURCE_CONFLICTED");
    }

    [TestMethod]
    public async Task AdminList_FiltersByStatus()
    {
        await using var db = CreateInMemoryDb();
        var pending = NewRestaurant("Pending Place");
        var approved = NewRestaurant("Approved Place");
        approved.Approve();
        db.Restaurants.AddRange(pending, approved);
        await db.SaveChangesAsync();
        var handler = new RestaurantQueryHandler(db);

        var result = await handler.GetAdminListAsync("Pending", 1, 20);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TotalCount.Should().Be(1);
        result.Data.Items.Single().Name.Should().Be("Pending Place");
    }

    [TestMethod]
    public async Task AdminList_InvalidStatus_Returns400()
    {
        await using var db = CreateInMemoryDb();
        var handler = new RestaurantQueryHandler(db);

        var result = await handler.GetAdminListAsync("NotAStatus", 1, 20);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("INVALID_STATUS");
    }
}
