using FluentAssertions;
using Gruuber.Restaurants.Application.Commands;
using Gruuber.Restaurants.Domain;
using Gruuber.Restaurants.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class RestaurantOwnerHandlerTests
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
    public async Task Update_ByOwnerWithCorrectVersion_Succeeds()
    {
        var (db, restaurant, ownerId) = SeedAsync().Result;
        await using var _ = db;
        var handler = new UpdateRestaurantHandler(db);

        var result = await handler.HandleAsync(new UpdateRestaurantCommand(
            restaurant.Id, ownerId, 1, "New Name", "nd", "BBQ", "new addr", 14.6, 121.0));

        result.IsSuccess.Should().BeTrue();
        result.Data!.Version.Should().Be(2);
        (await db.Restaurants.SingleAsync()).Name.Should().Be("New Name");
    }

    [TestMethod]
    public async Task Update_ByNonOwner_Returns403()
    {
        var (db, restaurant, _) = SeedAsync().Result;
        await using var _1 = db;
        var handler = new UpdateRestaurantHandler(db);

        var result = await handler.HandleAsync(new UpdateRestaurantCommand(
            restaurant.Id, Guid.NewGuid(), 1, "X", "d", "c", "a", 14.6, 121.0));

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("FORBIDDEN");
    }

    [TestMethod]
    public async Task Update_WithStaleVersion_Returns409()
    {
        var (db, restaurant, ownerId) = SeedAsync().Result;
        await using var _ = db;
        var handler = new UpdateRestaurantHandler(db);

        var result = await handler.HandleAsync(new UpdateRestaurantCommand(
            restaurant.Id, ownerId, 99, "X", "d", "c", "a", 14.6, 121.0));

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("RESOURCE_CONFLICTED");
    }

    [TestMethod]
    public async Task Update_UnknownRestaurant_Returns404()
    {
        await using var db = CreateInMemoryDb();
        var handler = new UpdateRestaurantHandler(db);

        var result = await handler.HandleAsync(new UpdateRestaurantCommand(
            Guid.NewGuid(), Guid.NewGuid(), 1, "X", "d", "c", "a", 14.6, 121.0));

        result.StatusCode.Should().Be(404);
    }

    [TestMethod]
    public async Task SetOpen_ByOwner_TogglesFlag()
    {
        var (db, restaurant, ownerId) = SeedAsync().Result;
        await using var _ = db;
        var handler = new SetRestaurantOpenHandler(db);

        var result = await handler.HandleAsync(new SetRestaurantOpenCommand(restaurant.Id, ownerId, 1, true));

        result.IsSuccess.Should().BeTrue();
        result.Data!.IsOpen.Should().BeTrue();
        result.Data.Version.Should().Be(2);
    }

    [TestMethod]
    public async Task SetOpen_ByNonOwner_Returns403()
    {
        var (db, restaurant, _) = SeedAsync().Result;
        await using var _1 = db;
        var handler = new SetRestaurantOpenHandler(db);

        var result = await handler.HandleAsync(new SetRestaurantOpenCommand(restaurant.Id, Guid.NewGuid(), 1, true));

        result.StatusCode.Should().Be(403);
    }
}
