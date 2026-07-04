using FluentAssertions;
using Gruuber.Restaurants.Domain;
using Gruuber.Restaurants.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class RestaurantCatalogReaderTests
{
    private static RestaurantsDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<RestaurantsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new RestaurantsDbContext(opts);
    }

    [TestMethod]
    public async Task GetRestaurant_ReturnsStatusOpenAndLocation()
    {
        await using var db = CreateInMemoryDb();
        var restaurant = Restaurant.Create(Guid.NewGuid(), "Kanto Grill", "d", "Filipino", "a", 14.5, 120.9, 1);
        restaurant.Approve();
        restaurant.SetOpen(true);
        db.Restaurants.Add(restaurant);
        await db.SaveChangesAsync();
        var reader = new RestaurantCatalogReader(db);

        var result = await reader.GetRestaurantAsync(restaurant.Id);

        result.Should().NotBeNull();
        result!.ApprovalStatus.Should().Be("Approved");
        result.IsOpen.Should().BeTrue();
        result.Lat.Should().Be(14.5);
        result.RegionId.Should().Be(1);
    }

    [TestMethod]
    public async Task GetRestaurant_Unknown_ReturnsNull()
    {
        await using var db = CreateInMemoryDb();
        var reader = new RestaurantCatalogReader(db);

        var result = await reader.GetRestaurantAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task GetMenuItems_ReturnsOnlyRequestedIds()
    {
        await using var db = CreateInMemoryDb();
        var restaurantId = Guid.NewGuid();
        var a = MenuItem.Create(restaurantId, "Pork BBQ", "d", "Grill", 120m, "PHP", 1);
        var b = MenuItem.Create(restaurantId, "Sisig", "d", "Mains", 150m, "PHP", 1);
        var c = MenuItem.Create(restaurantId, "Halo-Halo", "d", "Dessert", 90m, "PHP", 1);
        db.MenuItems.AddRange(a, b, c);
        await db.SaveChangesAsync();
        var reader = new RestaurantCatalogReader(db);

        var result = await reader.GetMenuItemsAsync(new[] { a.Id, c.Id });

        result.Should().HaveCount(2);
        result.Select(i => i.Name).Should().BeEquivalentTo("Pork BBQ", "Halo-Halo");
        result.Single(i => i.Name == "Pork BBQ").Price.Should().Be(120m);
    }
}
