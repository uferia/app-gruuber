using FluentAssertions;
using Gruuber.Restaurants.Application.Commands;
using Gruuber.Restaurants.Domain;
using Gruuber.Restaurants.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class MenuItemHandlerTests
{
    private static RestaurantsDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<RestaurantsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new RestaurantsDbContext(opts);
    }

    private static async Task<(RestaurantsDbContext db, Restaurant restaurant, Guid ownerId)> SeedAsync()
    {
        var db = CreateInMemoryDb();
        var ownerId = Guid.NewGuid();
        var restaurant = Restaurant.Create(ownerId, "Kanto Grill", "d", "Filipino", "a", 14.5, 120.9, 1);
        db.Restaurants.Add(restaurant);
        await db.SaveChangesAsync();
        return (db, restaurant, ownerId);
    }

    [TestMethod]
    public async Task Add_ByOwner_Creates201()
    {
        var (db, restaurant, ownerId) = await SeedAsync();
        await using var _ = db;
        var handler = new AddMenuItemHandler(db);

        var result = await handler.HandleAsync(new AddMenuItemCommand(
            restaurant.Id, ownerId, "Pork BBQ", "3 sticks", "Grill", 120.00m, "PHP"));

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data!.IsAvailable.Should().BeTrue();
        (await db.MenuItems.CountAsync()).Should().Be(1);
    }

    [TestMethod]
    public async Task Add_ByNonOwner_Returns403()
    {
        var (db, restaurant, _) = await SeedAsync();
        await using var _1 = db;
        var handler = new AddMenuItemHandler(db);

        var result = await handler.HandleAsync(new AddMenuItemCommand(
            restaurant.Id, Guid.NewGuid(), "Pork BBQ", "d", "Grill", 120.00m, "PHP"));

        result.StatusCode.Should().Be(403);
    }

    [TestMethod]
    public async Task Add_UnknownRestaurant_Returns404()
    {
        await using var db = CreateInMemoryDb();
        var handler = new AddMenuItemHandler(db);

        var result = await handler.HandleAsync(new AddMenuItemCommand(
            Guid.NewGuid(), Guid.NewGuid(), "Pork BBQ", "d", "Grill", 120.00m, "PHP"));

        result.StatusCode.Should().Be(404);
    }

    [TestMethod]
    public async Task Update_ByOwnerWithCorrectVersion_Succeeds()
    {
        var (db, restaurant, ownerId) = await SeedAsync();
        await using var _ = db;
        var item = MenuItem.Create(restaurant.Id, "Pork BBQ", "d", "Grill", 120.00m, "PHP", 1);
        db.MenuItems.Add(item);
        await db.SaveChangesAsync();
        var handler = new UpdateMenuItemHandler(db);

        var result = await handler.HandleAsync(new UpdateMenuItemCommand(
            restaurant.Id, item.Id, ownerId, 1, "Pork BBQ Large", "5 sticks", "Grill", 180.00m, false));

        result.IsSuccess.Should().BeTrue();
        result.Data!.Price.Should().Be(180.00m);
        result.Data.IsAvailable.Should().BeFalse();
        result.Data.Version.Should().Be(2);
    }

    [TestMethod]
    public async Task Update_WithStaleVersion_Returns409()
    {
        var (db, restaurant, ownerId) = await SeedAsync();
        await using var _ = db;
        var item = MenuItem.Create(restaurant.Id, "Pork BBQ", "d", "Grill", 120.00m, "PHP", 1);
        db.MenuItems.Add(item);
        await db.SaveChangesAsync();
        var handler = new UpdateMenuItemHandler(db);

        var result = await handler.HandleAsync(new UpdateMenuItemCommand(
            restaurant.Id, item.Id, ownerId, 99, "X", "d", "Grill", 180.00m, true));

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("RESOURCE_CONFLICTED");
    }

    [TestMethod]
    public async Task Delete_ByOwner_RemovesItem()
    {
        var (db, restaurant, ownerId) = await SeedAsync();
        await using var _ = db;
        var item = MenuItem.Create(restaurant.Id, "Pork BBQ", "d", "Grill", 120.00m, "PHP", 1);
        db.MenuItems.Add(item);
        await db.SaveChangesAsync();
        var handler = new DeleteMenuItemHandler(db);

        var result = await handler.HandleAsync(new DeleteMenuItemCommand(restaurant.Id, item.Id, ownerId));

        result.IsSuccess.Should().BeTrue();
        (await db.MenuItems.CountAsync()).Should().Be(0);
    }

    [TestMethod]
    public async Task Delete_ByNonOwner_Returns403()
    {
        var (db, restaurant, _) = await SeedAsync();
        await using var _1 = db;
        var item = MenuItem.Create(restaurant.Id, "Pork BBQ", "d", "Grill", 120.00m, "PHP", 1);
        db.MenuItems.Add(item);
        await db.SaveChangesAsync();
        var handler = new DeleteMenuItemHandler(db);

        var result = await handler.HandleAsync(new DeleteMenuItemCommand(restaurant.Id, item.Id, Guid.NewGuid()));

        result.StatusCode.Should().Be(403);
        (await db.MenuItems.CountAsync()).Should().Be(1);
    }
}
