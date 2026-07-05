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
    public async Task GetRestaurant_Approved_MapsOwnerStatusOpenAndLocation()
    {
        await using var db = CreateInMemoryDb();
        var ownerId = Guid.NewGuid();
        var restaurant = Restaurant.Create(ownerId, "Kanto Grill", "d", "Filipino", "a", 14.5, 120.9, 1);
        restaurant.Approve();
        restaurant.SetOpen(true);
        db.Restaurants.Add(restaurant);
        await db.SaveChangesAsync();
        var reader = new RestaurantCatalogReader(db);

        var result = await reader.GetRestaurantAsync(restaurant.Id);

        result.Should().NotBeNull();
        result!.OwnerUserId.Should().Be(ownerId);
        result.IsApproved.Should().BeTrue();
        result.IsOpen.Should().BeTrue();
        result.Lat.Should().Be(14.5);
        result.Lng.Should().Be(120.9);
        result.RegionId.Should().Be(1);
    }

    [TestMethod]
    public async Task GetRestaurant_Pending_IsApprovedFalse()
    {
        await using var db = CreateInMemoryDb();
        var restaurant = Restaurant.Create(Guid.NewGuid(), "Pending Place", "d", "Filipino", "a", 14.5, 120.9, 1);
        db.Restaurants.Add(restaurant);
        await db.SaveChangesAsync();
        var reader = new RestaurantCatalogReader(db);

        var result = await reader.GetRestaurantAsync(restaurant.Id);

        result!.IsApproved.Should().BeFalse();
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
