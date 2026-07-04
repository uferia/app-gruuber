using Gruuber.Restaurants.Domain;
using Gruuber.Restaurants.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Restaurants.Application.Commands;

public class AddMenuItemHandler
{
    private readonly RestaurantsDbContext _db;

    public AddMenuItemHandler(RestaurantsDbContext db)
    {
        _db = db;
    }

    public async Task<ApplicationResult<MenuItemResponse>> HandleAsync(
        AddMenuItemCommand command,
        CancellationToken cancellationToken = default)
    {
        var restaurant = await _db.Restaurants.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == command.RestaurantId, cancellationToken);
        if (restaurant is null)
            return ApplicationResult<MenuItemResponse>.Failure("NOT_FOUND", "Restaurant not found.", 404);

        if (restaurant.OwnerUserId != command.ActorUserId)
            return ApplicationResult<MenuItemResponse>.Failure("FORBIDDEN", "You do not own this restaurant.", 403);

        var item = MenuItem.Create(
            command.RestaurantId, command.Name, command.Description, command.Category,
            command.Price, command.Currency, restaurant.RegionId);
        _db.MenuItems.Add(item);
        await _db.SaveChangesAsync(cancellationToken);

        return ApplicationResult<MenuItemResponse>.Success(ToResponse(item), 201);
    }

    internal static MenuItemResponse ToResponse(MenuItem item) => new(
        item.Id, item.RestaurantId, item.Name, item.Description, item.Category,
        item.Price, item.Currency, item.IsAvailable, item.Version);
}

public class UpdateMenuItemHandler
{
    private readonly RestaurantsDbContext _db;

    public UpdateMenuItemHandler(RestaurantsDbContext db)
    {
        _db = db;
    }

    public async Task<ApplicationResult<MenuItemResponse>> HandleAsync(
        UpdateMenuItemCommand command,
        CancellationToken cancellationToken = default)
    {
        var restaurant = await _db.Restaurants.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == command.RestaurantId, cancellationToken);
        if (restaurant is null)
            return ApplicationResult<MenuItemResponse>.Failure("NOT_FOUND", "Restaurant not found.", 404);

        if (restaurant.OwnerUserId != command.ActorUserId)
            return ApplicationResult<MenuItemResponse>.Failure("FORBIDDEN", "You do not own this restaurant.", 403);

        var item = await _db.MenuItems.FirstOrDefaultAsync(
            m => m.Id == command.MenuItemId && m.RestaurantId == command.RestaurantId, cancellationToken);
        if (item is null)
            return ApplicationResult<MenuItemResponse>.Failure("NOT_FOUND", "Menu item not found.", 404);

        if (item.Version != command.ExpectedVersion)
            return ApplicationResult<MenuItemResponse>.Failure(
                "RESOURCE_CONFLICTED", "Menu item was modified by another request.", 409);

        item.Update(command.Name, command.Description, command.Category, command.Price, command.IsAvailable);
        await _db.SaveChangesAsync(cancellationToken);

        return ApplicationResult<MenuItemResponse>.Success(AddMenuItemHandler.ToResponse(item));
    }
}

public class DeleteMenuItemHandler
{
    private readonly RestaurantsDbContext _db;

    public DeleteMenuItemHandler(RestaurantsDbContext db)
    {
        _db = db;
    }

    public async Task<ApplicationResult<DeleteMenuItemResponse>> HandleAsync(
        DeleteMenuItemCommand command,
        CancellationToken cancellationToken = default)
    {
        var restaurant = await _db.Restaurants.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == command.RestaurantId, cancellationToken);
        if (restaurant is null)
            return ApplicationResult<DeleteMenuItemResponse>.Failure("NOT_FOUND", "Restaurant not found.", 404);

        if (restaurant.OwnerUserId != command.ActorUserId)
            return ApplicationResult<DeleteMenuItemResponse>.Failure("FORBIDDEN", "You do not own this restaurant.", 403);

        var item = await _db.MenuItems.FirstOrDefaultAsync(
            m => m.Id == command.MenuItemId && m.RestaurantId == command.RestaurantId, cancellationToken);
        if (item is null)
            return ApplicationResult<DeleteMenuItemResponse>.Failure("NOT_FOUND", "Menu item not found.", 404);

        _db.MenuItems.Remove(item);
        await _db.SaveChangesAsync(cancellationToken);

        return ApplicationResult<DeleteMenuItemResponse>.Success(new DeleteMenuItemResponse(item.Id));
    }
}
