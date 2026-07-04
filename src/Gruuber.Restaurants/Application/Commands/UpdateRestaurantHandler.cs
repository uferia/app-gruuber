using Gruuber.Restaurants.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Restaurants.Application.Commands;

public class UpdateRestaurantHandler
{
    private readonly RestaurantsDbContext _db;

    public UpdateRestaurantHandler(RestaurantsDbContext db)
    {
        _db = db;
    }

    public async Task<ApplicationResult<UpdateRestaurantResponse>> HandleAsync(
        UpdateRestaurantCommand command,
        CancellationToken cancellationToken = default)
    {
        var restaurant = await _db.Restaurants.FirstOrDefaultAsync(r => r.Id == command.RestaurantId, cancellationToken);
        if (restaurant is null)
            return ApplicationResult<UpdateRestaurantResponse>.Failure("NOT_FOUND", "Restaurant not found.", 404);

        if (restaurant.OwnerUserId != command.ActorUserId)
            return ApplicationResult<UpdateRestaurantResponse>.Failure("FORBIDDEN", "You do not own this restaurant.", 403);

        if (restaurant.Version != command.ExpectedVersion)
            return ApplicationResult<UpdateRestaurantResponse>.Failure(
                "RESOURCE_CONFLICTED", "Restaurant was modified by another request.", 409);

        restaurant.UpdateProfile(command.Name, command.Description, command.CuisineType,
            command.Address, command.Lat, command.Lng);
        await _db.SaveChangesAsync(cancellationToken);

        return ApplicationResult<UpdateRestaurantResponse>.Success(
            new UpdateRestaurantResponse(restaurant.Id, restaurant.Version));
    }
}
