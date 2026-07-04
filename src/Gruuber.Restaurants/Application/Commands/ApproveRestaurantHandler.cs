using Gruuber.Restaurants.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Restaurants.Application.Commands;

public class ApproveRestaurantHandler
{
    private readonly RestaurantsDbContext _db;

    public ApproveRestaurantHandler(RestaurantsDbContext db)
    {
        _db = db;
    }

    public async Task<ApplicationResult<ApproveRestaurantResponse>> HandleAsync(
        ApproveRestaurantCommand command,
        CancellationToken cancellationToken = default)
    {
        var restaurant = await _db.Restaurants.FirstOrDefaultAsync(r => r.Id == command.RestaurantId, cancellationToken);
        if (restaurant is null)
            return ApplicationResult<ApproveRestaurantResponse>.Failure("NOT_FOUND", "Restaurant not found.", 404);

        if (restaurant.Version != command.ExpectedVersion)
            return ApplicationResult<ApproveRestaurantResponse>.Failure(
                "RESOURCE_CONFLICTED", "Restaurant was modified by another request.", 409);

        restaurant.Approve();
        await _db.SaveChangesAsync(cancellationToken);

        return ApplicationResult<ApproveRestaurantResponse>.Success(
            new ApproveRestaurantResponse(restaurant.Id, restaurant.ApprovalStatus.ToString(), restaurant.ApprovedAt!.Value));
    }
}
