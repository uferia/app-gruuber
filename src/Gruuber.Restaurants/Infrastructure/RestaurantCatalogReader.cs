using Gruuber.SharedKernel.Catalog;
using Gruuber.Restaurants.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Restaurants.Infrastructure;

public class RestaurantCatalogReader : IRestaurantCatalogReader
{
    private readonly RestaurantsDbContext _db;

    public RestaurantCatalogReader(RestaurantsDbContext db)
    {
        _db = db;
    }

    public async Task<CatalogRestaurant?> GetRestaurantAsync(Guid restaurantId, CancellationToken cancellationToken = default)
    {
        return await _db.Restaurants.AsNoTracking()
            .Where(r => r.Id == restaurantId)
            .Select(r => new CatalogRestaurant(
                r.Id, r.OwnerUserId, r.Name,
                r.ApprovalStatus == RestaurantApprovalStatus.Approved,
                r.IsOpen, r.RegionId, r.Lat, r.Lng))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogMenuItem>> GetMenuItemsAsync(
        IReadOnlyCollection<Guid> menuItemIds,
        CancellationToken cancellationToken = default)
    {
        return await _db.MenuItems.AsNoTracking()
            .Where(m => menuItemIds.Contains(m.Id))
            .Select(m => new CatalogMenuItem(m.Id, m.RestaurantId, m.Name, m.Price, m.Currency, m.IsAvailable))
            .ToListAsync(cancellationToken);
    }
}
