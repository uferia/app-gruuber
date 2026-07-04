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
