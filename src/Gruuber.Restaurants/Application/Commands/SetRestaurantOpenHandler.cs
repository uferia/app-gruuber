using Gruuber.Restaurants.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Restaurants.Application.Commands;

public class SetRestaurantOpenHandler
{
    private readonly RestaurantsDbContext _db;

    public SetRestaurantOpenHandler(RestaurantsDbContext db)
    {
        _db = db;
    }

    public async Task<ApplicationResult<SetRestaurantOpenResponse>> HandleAsync(
        SetRestaurantOpenCommand command,
        CancellationToken cancellationToken = default)
    {
        var restaurant = await _db.Restaurants.FirstOrDefaultAsync(r => r.Id == command.RestaurantId, cancellationToken);
        if (restaurant is null)
            return ApplicationResult<SetRestaurantOpenResponse>.Failure("NOT_FOUND", "Restaurant not found.", 404);

        if (restaurant.OwnerUserId != command.ActorUserId)
            return ApplicationResult<SetRestaurantOpenResponse>.Failure("FORBIDDEN", "You do not own this restaurant.", 403);

        if (restaurant.Version != command.ExpectedVersion)
            return ApplicationResult<SetRestaurantOpenResponse>.Failure(
                "RESOURCE_CONFLICTED", "Restaurant was modified by another request.", 409);

        restaurant.SetOpen(command.IsOpen);
        await _db.SaveChangesAsync(cancellationToken);

        return ApplicationResult<SetRestaurantOpenResponse>.Success(
            new SetRestaurantOpenResponse(restaurant.Id, restaurant.IsOpen, restaurant.Version));
    }
}
