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

public record AddMenuItemCommand(
    Guid RestaurantId,
    Guid ActorUserId,
    string Name,
    string Description,
    string Category,
    decimal Price,
    string Currency);

public record UpdateMenuItemCommand(
    Guid RestaurantId,
    Guid MenuItemId,
    Guid ActorUserId,
    long ExpectedVersion,
    string Name,
    string Description,
    string Category,
    decimal Price,
    bool IsAvailable);

public record DeleteMenuItemCommand(Guid RestaurantId, Guid MenuItemId, Guid ActorUserId);

public record MenuItemResponse(
    Guid MenuItemId,
    Guid RestaurantId,
    string Name,
    string Description,
    string Category,
    decimal Price,
    string Currency,
    bool IsAvailable,
    long Version);

public record DeleteMenuItemResponse(Guid MenuItemId);

public record ApproveRestaurantCommand(Guid RestaurantId, long ExpectedVersion);

public record ApproveRestaurantResponse(Guid RestaurantId, string ApprovalStatus, DateTime ApprovedAt);

public record RejectRestaurantCommand(Guid RestaurantId, long ExpectedVersion, string Reason);

public record RejectRestaurantResponse(Guid RestaurantId, string ApprovalStatus, string Reason);
