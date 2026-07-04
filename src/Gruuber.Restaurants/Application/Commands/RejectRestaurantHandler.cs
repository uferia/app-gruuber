using Gruuber.Restaurants.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Restaurants.Application.Commands;

public class RejectRestaurantHandler
{
    private readonly RestaurantsDbContext _db;

    public RejectRestaurantHandler(RestaurantsDbContext db)
    {
        _db = db;
    }

    public async Task<ApplicationResult<RejectRestaurantResponse>> HandleAsync(
        RejectRestaurantCommand command,
        CancellationToken cancellationToken = default)
    {
        var restaurant = await _db.Restaurants.FirstOrDefaultAsync(r => r.Id == command.RestaurantId, cancellationToken);
        if (restaurant is null)
            return ApplicationResult<RejectRestaurantResponse>.Failure("NOT_FOUND", "Restaurant not found.", 404);

        if (restaurant.Version != command.ExpectedVersion)
            return ApplicationResult<RejectRestaurantResponse>.Failure(
                "RESOURCE_CONFLICTED", "Restaurant was modified by another request.", 409);

        restaurant.Reject(command.Reason);
        await _db.SaveChangesAsync(cancellationToken);

        return ApplicationResult<RejectRestaurantResponse>.Success(
            new RejectRestaurantResponse(restaurant.Id, restaurant.ApprovalStatus.ToString(), command.Reason));
    }
}
