using FluentAssertions;
using Gruuber.Restaurants.Application.Queries;
using Gruuber.Restaurants.Domain;
using Gruuber.Restaurants.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class RestaurantQueryHandlerTests
{
    private static RestaurantsDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<RestaurantsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new RestaurantsDbContext(opts);
    }

    private static Restaurant Approved(string name, string cuisine, double lat, double lng, int regionId = 1, bool open = true)
    {
        var r = Restaurant.Create(Guid.NewGuid(), name, "d", cuisine, "a", lat, lng, regionId);
        r.Approve();
        if (open) r.SetOpen(true);
        return r;
    }

    [TestMethod]
    public async Task Discover_ExcludesPendingAndOtherRegions()
    {
        await using var db = CreateInMemoryDb();
        db.Restaurants.AddRange(
            Approved("In Region", "Filipino", 14.5, 120.9),
            Approved("Other Region", "Filipino", 14.5, 120.9, regionId: 2),
            Restaurant.Create(Guid.NewGuid(), "Pending Place", "d", "Filipino", "a", 14.5, 120.9, 1));
        await db.SaveChangesAsync();
        var handler = new RestaurantQueryHandler(db);

        var result = await handler.DiscoverAsync(new DiscoverRestaurantsQuery(1, null, null, null, false, 1, 20));

        result.Data!.TotalCount.Should().Be(1);
        result.Data.Items.Single().Name.Should().Be("In Region");
    }

    [TestMethod]
    public async Task Discover_WithCoordinates_SortsByDistance()
    {
        await using var db = CreateInMemoryDb();
        // Caller at (14.60, 120.98). "Near" ~0km, "Far" ~15km away.
        db.Restaurants.AddRange(
            Approved("Far", "Filipino", 14.74, 120.98),
            Approved("Near", "Filipino", 14.60, 120.98));
        await db.SaveChangesAsync();
        var handler = new RestaurantQueryHandler(db);

        var result = await handler.DiscoverAsync(new DiscoverRestaurantsQuery(1, 14.60, 120.98, null, false, 1, 20));

        result.Data!.Items.Should().HaveCount(2);
        result.Data.Items[0].Name.Should().Be("Near");
        result.Data.Items[0].DistanceKm.Should().BeLessThan(1);
        result.Data.Items[1].DistanceKm.Should().BeGreaterThan(10);
    }

    [TestMethod]
    public async Task Discover_SearchMatchesNameOrCuisine_CaseInsensitive()
    {
        await using var db = CreateInMemoryDb();
        db.Restaurants.AddRange(
            Approved("Kanto Grill", "Filipino", 14.5, 120.9),
            Approved("Sushi Ya", "Japanese", 14.5, 120.9));
        await db.SaveChangesAsync();
        var handler = new RestaurantQueryHandler(db);

        var byName = await handler.DiscoverAsync(new DiscoverRestaurantsQuery(1, null, null, "kanto", false, 1, 20));
        var byCuisine = await handler.DiscoverAsync(new DiscoverRestaurantsQuery(1, null, null, "JAPANESE", false, 1, 20));

        byName.Data!.Items.Single().Name.Should().Be("Kanto Grill");
        byCuisine.Data!.Items.Single().Name.Should().Be("Sushi Ya");
    }

    [TestMethod]
    public async Task Discover_OpenNow_ExcludesClosed()
    {
        await using var db = CreateInMemoryDb();
        db.Restaurants.AddRange(
            Approved("Open Place", "Filipino", 14.5, 120.9),
            Approved("Closed Place", "Filipino", 14.5, 120.9, open: false));
        await db.SaveChangesAsync();
        var handler = new RestaurantQueryHandler(db);

        var result = await handler.DiscoverAsync(new DiscoverRestaurantsQuery(1, null, null, null, true, 1, 20));

        result.Data!.Items.Single().Name.Should().Be("Open Place");
    }

    [TestMethod]
    public async Task Discover_Paginates()
    {
        await using var db = CreateInMemoryDb();
        for (var i = 0; i < 25; i++)
            db.Restaurants.Add(Approved($"Place {i:D2}", "Filipino", 14.5, 120.9));
        await db.SaveChangesAsync();
        var handler = new RestaurantQueryHandler(db);

        var page2 = await handler.DiscoverAsync(new DiscoverRestaurantsQuery(1, null, null, null, false, 2, 10));

        page2.Data!.TotalCount.Should().Be(25);
        page2.Data.Items.Should().HaveCount(10);
        page2.Data.Page.Should().Be(2);
    }

    [TestMethod]
    public async Task Discover_ExcludesNonApprovedStatuses()
    {
        await using var db = CreateInMemoryDb();
        var pending = Restaurant.Create(Guid.NewGuid(), "Pending Place", "d", "Filipino", "a", 14.5, 120.9, 1);
        var rejected = Restaurant.Create(Guid.NewGuid(), "Rejected Place", "d", "Filipino", "a", 14.5, 120.9, 1);
        rejected.Reject("Incomplete documents");
        db.Restaurants.AddRange(
            Approved("Approved Place", "Filipino", 14.5, 120.9),
            pending,
            rejected);
        await db.SaveChangesAsync();
        var handler = new RestaurantQueryHandler(db);

        var result = await handler.DiscoverAsync(new DiscoverRestaurantsQuery(1, null, null, null, false, 1, 20));
        var rejectedDetail = await handler.GetPublicAsync(rejected.Id);

        result.Data!.TotalCount.Should().Be(1);
        result.Data.Items.Single().Name.Should().Be("Approved Place");
        rejectedDetail.StatusCode.Should().Be(404);
    }

    [TestMethod]
    public async Task GetPublic_PendingRestaurant_Returns404()
    {
        await using var db = CreateInMemoryDb();
        var pending = Restaurant.Create(Guid.NewGuid(), "Pending Place", "d", "Filipino", "a", 14.5, 120.9, 1);
        db.Restaurants.Add(pending);
        await db.SaveChangesAsync();
        var handler = new RestaurantQueryHandler(db);

        var result = await handler.GetPublicAsync(pending.Id);

        result.StatusCode.Should().Be(404);
    }

    [TestMethod]
    public async Task GetMenu_ReturnsItemsWithAvailabilityFlag()
    {
        await using var db = CreateInMemoryDb();
        var restaurant = Approved("Kanto Grill", "Filipino", 14.5, 120.9);
        db.Restaurants.Add(restaurant);
        var soldOut = MenuItem.Create(restaurant.Id, "Sisig", "d", "Mains", 150m, "PHP", 1);
        soldOut.Update("Sisig", "d", "Mains", 150m, false);
        db.MenuItems.AddRange(
            MenuItem.Create(restaurant.Id, "Pork BBQ", "d", "Grill", 120m, "PHP", 1),
            soldOut);
        await db.SaveChangesAsync();
        var handler = new RestaurantQueryHandler(db);

        var result = await handler.GetMenuAsync(restaurant.Id);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().HaveCount(2);
        result.Data!.Single(i => i.Name == "Sisig").IsAvailable.Should().BeFalse();
    }
}
