using Gruuber.Restaurants.Domain;
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

    public async Task<ApplicationResult<PagedResponse<AdminRestaurantListItem>>> GetAdminListAsync(
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var effectivePage = Math.Max(1, page);
        var effectivePageSize = Math.Clamp(pageSize, 1, 50);

        var query = _db.Restaurants.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<RestaurantApprovalStatus>(status, ignoreCase: true, out var parsed))
                return ApplicationResult<PagedResponse<AdminRestaurantListItem>>.Failure(
                    "INVALID_STATUS", $"Status must be one of: {string.Join(", ", Enum.GetNames<RestaurantApprovalStatus>())}.", 400);
            query = query.Where(r => r.ApprovalStatus == parsed);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(r => r.CreatedAt)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .Select(r => new AdminRestaurantListItem(
                r.Id, r.Name, r.CuisineType, r.RegionId, r.ApprovalStatus.ToString(), r.CreatedAt, r.Version))
            .ToListAsync(cancellationToken);

        return ApplicationResult<PagedResponse<AdminRestaurantListItem>>.Success(
            new PagedResponse<AdminRestaurantListItem>(items, effectivePage, effectivePageSize, total));
    }
}
