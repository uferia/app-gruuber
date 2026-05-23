namespace Gruuber.Rides.Application.Commands;

public record RequestRideCommand(Guid RiderId, string RideType, double PickupLat, double PickupLng, int RegionId);
public record RequestRideResponse(Guid RideId, string Status, string Message);

public record MatchDriverCommand(Guid RideId, long ExpectedVersion, int RegionId);

public record TransitionRideCommand(Guid RideId, string NewStatus, long ExpectedVersion, int RegionId, Guid ActorId);
public record TransitionRideResponse(Guid RideId, string Status);
