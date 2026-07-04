namespace Gruuber.Restaurants.Application.Queries;

public record RestaurantDetailResponse(
    Guid Id,
    string Name,
    string Description,
    string CuisineType,
    string Address,
    double Lat,
    double Lng,
    string ApprovalStatus,
    string? RejectionReason,
    bool IsOpen,
    int RegionId,
    long Version);

public record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public record AdminRestaurantListItem(
    Guid Id,
    string Name,
    string CuisineType,
    int RegionId,
    string ApprovalStatus,
    DateTime CreatedAt,
    long Version);

public record DiscoverRestaurantsQuery(
    int RegionId,
    double? Lat,
    double? Lng,
    string? Search,
    bool OpenNow,
    int Page,
    int PageSize);

public record RestaurantListItem(
    Guid Id,
    string Name,
    string CuisineType,
    string Address,
    bool IsOpen,
    double? DistanceKm);

public record PublicRestaurantResponse(
    Guid Id,
    string Name,
    string Description,
    string CuisineType,
    string Address,
    double Lat,
    double Lng,
    bool IsOpen);

public record PublicMenuItem(
    Guid Id,
    string Name,
    string Description,
    string Category,
    decimal Price,
    string Currency,
    bool IsAvailable);
