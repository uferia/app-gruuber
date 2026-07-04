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
