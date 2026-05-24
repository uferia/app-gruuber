using Gruuber.SharedKernel.Pricing;

namespace Gruuber.Rides.Application.Commands;

public record RequestRideCommand(
    Guid RiderId,
    string RideType,
    double PickupLat,
    double PickupLng,
    int RegionId,
    double? DestLat = null,
    double? DestLng = null);

public record RequestRideResponse(
    Guid RideId,
    string Status,
    string Message,
    FareEstimate? Fare = null,
    int? MatchTimeoutSecs = null,
    decimal? DiscountedFareEstimate = null);

public record MatchDriverCommand(Guid RideId, long ExpectedVersion, int RegionId);

public record TransitionRideCommand(Guid RideId, string NewStatus, long ExpectedVersion, int RegionId, Guid ActorId);
public record TransitionRideResponse(Guid RideId, string Status);

public record AcceptSoloUpgradeCommand(Guid RideId, long ExpectedVersion, Guid RiderId, int RegionId);
public record AcceptSoloUpgradeResponse(Guid RideId, string Status);
