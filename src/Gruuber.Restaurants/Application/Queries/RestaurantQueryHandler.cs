using Gruuber.Restaurants.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Restaurants.Application.Queries;

public class RestaurantQueryHandler
{
    private readonly RestaurantsDbContext _db;

    public RestaurantQueryHandler(RestaurantsDbContext db)
    {
        _db = db;
    }

    public async Task<ApplicationResult<RestaurantDetailResponse>> GetMineAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var r = await _db.Restaurants.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OwnerUserId == ownerUserId, cancellationToken);
        if (r is null)
            return ApplicationResult<RestaurantDetailResponse>.Failure(
                "NOT_FOUND", "No restaurant registered for this account.", 404);

        return ApplicationResult<RestaurantDetailResponse>.Success(new RestaurantDetailResponse(
            r.Id, r.Name, r.Description, r.CuisineType, r.Address, r.Lat, r.Lng,
            r.ApprovalStatus.ToString(), r.RejectionReason, r.IsOpen, r.RegionId, r.Version));
    }
}
