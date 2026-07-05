namespace Gruuber.SharedKernel.Catalog;

public record CatalogRestaurant(Guid Id, Guid OwnerUserId, string Name, bool IsApproved, bool IsOpen, int RegionId, double Lat, double Lng);
public record CatalogMenuItem(Guid Id, Guid RestaurantId, string Name, decimal Price, string Currency, bool IsAvailable);

public interface IRestaurantCatalogReader
{
    Task<CatalogRestaurant?> GetRestaurantAsync(Guid restaurantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogMenuItem>> GetMenuItemsAsync(IReadOnlyCollection<Guid> menuItemIds, CancellationToken cancellationToken = default);
}
