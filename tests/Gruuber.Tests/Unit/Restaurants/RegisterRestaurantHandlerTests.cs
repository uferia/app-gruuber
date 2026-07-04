using FluentAssertions;
using Gruuber.Restaurants.Application.Commands;
using Gruuber.Restaurants.Application.Queries;
using Gruuber.Restaurants.Domain;
using Gruuber.Restaurants.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class RegisterRestaurantHandlerTests
{
    private static RestaurantsDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<RestaurantsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new RestaurantsDbContext(opts);
    }

    private static RegisterRestaurantCommand NewCommand(Guid? ownerId = null) =>
        new(ownerId ?? Guid.NewGuid(), "Kanto Grill", "Filipino BBQ", "Filipino", "123 Mabini St", 14.5995, 120.9842, 1);

    [TestMethod]
    public async Task Register_CreatesPendingRestaurant_Returns201()
    {
        await using var db = CreateInMemoryDb();
        var handler = new RegisterRestaurantHandler(db);

        var result = await handler.HandleAsync(NewCommand());

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data!.ApprovalStatus.Should().Be("Pending");
        (await db.Restaurants.CountAsync()).Should().Be(1);
    }

    [TestMethod]
    public async Task Register_SecondRestaurantForSameOwner_Returns409()
    {
        await using var db = CreateInMemoryDb();
        var handler = new RegisterRestaurantHandler(db);
        var ownerId = Guid.NewGuid();
        await handler.HandleAsync(NewCommand(ownerId));

        var result = await handler.HandleAsync(NewCommand(ownerId));

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("RESTAURANT_ALREADY_EXISTS");
    }

    private sealed class ThrowingSaveDbContext : RestaurantsDbContext
    {
        public ThrowingSaveDbContext(DbContextOptions<RestaurantsDbContext> options) : base(options) { }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => throw new DbUpdateException("unique constraint violation");
    }

    [TestMethod]
    public async Task Register_UniqueIndexRace_Returns409()
    {
        var opts = new DbContextOptionsBuilder<RestaurantsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ThrowingSaveDbContext(opts);
        var handler = new RegisterRestaurantHandler(db);

        var result = await handler.HandleAsync(NewCommand());

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("RESTAURANT_ALREADY_EXISTS");
    }

    [TestMethod]
    public async Task GetMine_ReturnsOwnRestaurant()
    {
        await using var db = CreateInMemoryDb();
        var ownerId = Guid.NewGuid();
        db.Restaurants.Add(Restaurant.Create(ownerId, "Kanto Grill", "d", "Filipino", "a", 14.5, 120.9, 1));
        await db.SaveChangesAsync();
        var handler = new RestaurantQueryHandler(db);

        var result = await handler.GetMineAsync(ownerId);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Name.Should().Be("Kanto Grill");
        result.Data.ApprovalStatus.Should().Be("Pending");
    }

    [TestMethod]
    public async Task GetMine_NoRestaurant_Returns404()
    {
        await using var db = CreateInMemoryDb();
        var handler = new RestaurantQueryHandler(db);

        var result = await handler.GetMineAsync(Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("NOT_FOUND");
    }
}
