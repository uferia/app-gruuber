namespace Gruuber.Restaurants.Application.Commands;

public record RegisterRestaurantCommand(
    Guid OwnerUserId,
    string Name,
    string Description,
    string CuisineType,
    string Address,
    double Lat,
    double Lng,
    int RegionId);

public record RegisterRestaurantResponse(Guid RestaurantId, string ApprovalStatus);
