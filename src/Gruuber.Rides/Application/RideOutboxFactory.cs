using System.Text.Json;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;

namespace Gruuber.Rides.Application;

public sealed class RideOutboxFactory
{
    public RideOutboxEntry CreateRideRequested(int regionId, Guid rideId, Guid riderId,
        double pickupLat, double pickupLng, decimal surgeMultiplier, decimal? finalFare) =>
        Build(regionId, new
        {
            EventName = "ride_requested",
            RideId = rideId,
            RiderId = riderId,
            PickupLat = pickupLat,
            PickupLng = pickupLng,
            RegionId = regionId,
            SurgeMultiplier = surgeMultiplier,
            FinalFare = finalFare,
            OccurredAt = DateTime.UtcNow
        });

    public RideOutboxEntry CreateRidePoolQueued(int regionId, Guid rideId, Guid riderId,
        double pickupLat, double pickupLng, double? destLat, double? destLng) =>
        Build(regionId, new
        {
            EventName = "ride_pool_queued",
            RideId = rideId,
            RiderId = riderId,
            RegionId = regionId,
            Origin = new { Lat = pickupLat, Lng = pickupLng },
            Destination = new { Lat = destLat, Lng = destLng },
            RequestedAt = DateTime.UtcNow
        });

    public RideOutboxEntry CreateDriverMatched(int regionId, Guid rideId, Guid driverId, double score) =>
        Build(regionId, new
        {
            EventName = "driver_matched",
            RideId = rideId,
            DriverId = driverId,
            Score = score,
            RegionId = regionId,
            OccurredAt = DateTime.UtcNow
        });

    public RideOutboxEntry CreateRideStatusChanged(int regionId, Guid rideId, string newStatus, Guid actorId) =>
        Build(regionId, new
        {
            EventName = "ride_status_changed",
            RideId = rideId,
            NewStatus = newStatus,
            ActorId = actorId,
            RegionId = regionId,
            OccurredAt = DateTime.UtcNow
        });

    public RideOutboxEntry CreateRidePoolUpgraded(int regionId, Guid rideId, Guid riderId) =>
        Build(regionId, new
        {
            EventName = "ride_pool_upgraded",
            RideId = rideId,
            RiderId = riderId,
            RegionId = regionId,
            PreviousStatus = "pool_queued",
            OccurredAt = DateTime.UtcNow
        });

    public RideOutboxEntry CreateRidePoolTimeout(int regionId, Ride ride) =>
        Build(regionId, new
        {
            EventName = "ride_pool_timeout",
            RideId = ride.Id,
            RiderId = ride.RiderId,
            RegionId = regionId,
            Reason = "no_match",
            NotifyUser = true,
            OccurredAt = DateTime.UtcNow
        });

    public RideOutboxEntry CreateRidePoolMatched(int regionId, Guid poolTripId, Guid thisRideId, Guid otherRideId) =>
        Build(regionId, new
        {
            EventName = "ride_pool_matched",
            PoolTripId = poolTripId,
            RideId = thisRideId,
            OtherRideId = otherRideId,
            RegionId = regionId,
            OccurredAt = DateTime.UtcNow
        });

    public RideOutboxEntry CreateRidePoolMatchFailed(int regionId, Guid rideId1, Guid rideId2, Guid missingRideId) =>
        Build(regionId, new
        {
            EventName = "ride_pool_match_failed",
            RideId1 = rideId1,
            RideId2 = rideId2,
            MissingRideId = missingRideId,
            RegionId = regionId,
            Reason = "ride_not_found",
            OccurredAt = DateTime.UtcNow
        });

    private static RideOutboxEntry Build(int regionId, object payload) =>
        new()
        {
            EventType = $"ride-events-{regionId}",
            Payload = JsonSerializer.Serialize(payload)
        };
}