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

    public async Task<ApplicationResult<PagedResponse<RestaurantListItem>>> DiscoverAsync(
        DiscoverRestaurantsQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 50);

        var dbQuery = _db.Restaurants.AsNoTracking()
            .Where(r => r.RegionId == query.RegionId && r.ApprovalStatus == RestaurantApprovalStatus.Approved);

        if (query.OpenNow)
            dbQuery = dbQuery.Where(r => r.IsOpen);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            dbQuery = dbQuery.Where(r => r.Name.ToLower().Contains(term) || r.CuisineType.ToLower().Contains(term));
        }

        // Region-scoped catalogs are small; distance sort happens in memory after SQL filtering.
        var filtered = await dbQuery.ToListAsync(cancellationToken);
        var total = filtered.Count;

        List<RestaurantListItem> ordered;
        if (query.Lat is double lat && query.Lng is double lng)
        {
            ordered = filtered
                .Select(r => new RestaurantListItem(
                    r.Id, r.Name, r.CuisineType, r.Address, r.IsOpen,
                    Math.Round(HaversineKm(lat, lng, r.Lat, r.Lng), 2)))
                .OrderBy(i => i.DistanceKm)
                .ToList();
        }
        else
        {
            ordered = filtered
                .OrderBy(r => r.Name)
                .Select(r => new RestaurantListItem(r.Id, r.Name, r.CuisineType, r.Address, r.IsOpen, null))
                .ToList();
        }

        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return ApplicationResult<PagedResponse<RestaurantListItem>>.Success(
            new PagedResponse<RestaurantListItem>(items, page, pageSize, total));
    }

    public async Task<ApplicationResult<PublicRestaurantResponse>> GetPublicAsync(
        Guid restaurantId,
        CancellationToken cancellationToken = default)
    {
        var r = await _db.Restaurants.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == restaurantId && x.ApprovalStatus == RestaurantApprovalStatus.Approved, cancellationToken);
        if (r is null)
            return ApplicationResult<PublicRestaurantResponse>.Failure("NOT_FOUND", "Restaurant not found.", 404);

        return ApplicationResult<PublicRestaurantResponse>.Success(new PublicRestaurantResponse(
            r.Id, r.Name, r.Description, r.CuisineType, r.Address, r.Lat, r.Lng, r.IsOpen));
    }

    public async Task<ApplicationResult<IReadOnlyList<PublicMenuItem>>> GetMenuAsync(
        Guid restaurantId,
        CancellationToken cancellationToken = default)
    {
        var exists = await _db.Restaurants.AsNoTracking()
            .AnyAsync(x => x.Id == restaurantId && x.ApprovalStatus == RestaurantApprovalStatus.Approved, cancellationToken);
        if (!exists)
            return ApplicationResult<IReadOnlyList<PublicMenuItem>>.Failure("NOT_FOUND", "Restaurant not found.", 404);

        var items = await _db.MenuItems.AsNoTracking()
            .Where(m => m.RestaurantId == restaurantId)
            .OrderBy(m => m.Category).ThenBy(m => m.Name)
            .Select(m => new PublicMenuItem(m.Id, m.Name, m.Description, m.Category, m.Price, m.Currency, m.IsAvailable))
            .ToListAsync(cancellationToken);

        return ApplicationResult<IReadOnlyList<PublicMenuItem>>.Success(items);
    }

    private static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double earthRadiusKm = 6371.0;
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLng = (lng2 - lng1) * Math.PI / 180.0;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return earthRadiusKm * 2 * Math.Asin(Math.Sqrt(a));
    }
}
