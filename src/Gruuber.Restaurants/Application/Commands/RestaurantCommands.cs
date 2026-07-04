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

public record UpdateRestaurantCommand(
    Guid RestaurantId,
    Guid ActorUserId,
    long ExpectedVersion,
    string Name,
    string Description,
    string CuisineType,
    string Address,
    double Lat,
    double Lng);

public record UpdateRestaurantResponse(Guid RestaurantId, long Version);

public record SetRestaurantOpenCommand(Guid RestaurantId, Guid ActorUserId, long ExpectedVersion, bool IsOpen);

public record SetRestaurantOpenResponse(Guid RestaurantId, bool IsOpen, long Version);
