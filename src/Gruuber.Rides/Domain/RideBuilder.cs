using Gruuber.Rides.Domain;

namespace Gruuber.Rides.Domain;

/// <summary>
/// Builder pattern — constructs a Ride aggregate with optional fields using a fluent API.
/// Separates construction complexity from the domain object, and makes tests/feature code readable.
/// </summary>
public sealed class RideBuilder
{
    private Guid _riderId;
    private string _rideType = "solo";
    private int _regionId;
    private double _pickupLat;
    private double _pickupLng;
    private double? _destLat;
    private double? _destLng;
    private decimal? _baseFare;
    private decimal _surgeMultiplier = 1.0m;
    private decimal? _finalFare;
    private string? _surgeReason;

    public RideBuilder ForRider(Guid riderId)
    {
        _riderId = riderId;
        return this;
    }

    public RideBuilder WithType(string rideType)
    {
        _rideType = rideType;
        return this;
    }

    public RideBuilder InRegion(int regionId)
    {
        _regionId = regionId;
        return this;
    }

    public RideBuilder FromPickup(double lat, double lng)
    {
        _pickupLat = lat;
        _pickupLng = lng;
        return this;
    }

    public RideBuilder ToDestination(double lat, double lng)
    {
        _destLat = lat;
        _destLng = lng;
        return this;
    }

    public RideBuilder WithFare(decimal baseFare, decimal surgeMultiplier = 1.0m, string? surgeReason = null)
    {
        _baseFare = baseFare;
        _surgeMultiplier = surgeMultiplier;
        _finalFare = baseFare * surgeMultiplier;
        _surgeReason = surgeReason;
        return this;
    }

    public Ride Build()
    {
        if (_riderId == Guid.Empty) throw new InvalidOperationException("RiderId is required.");
        if (_regionId == 0) throw new InvalidOperationException("RegionId is required.");

        return _rideType == "pool"
            ? Ride.CreatePool(_riderId, _regionId,
                _pickupLat, _pickupLng,
                _destLat ?? throw new InvalidOperationException("Pool rides require a destination."),
                _destLng!.Value,
                _baseFare, _surgeMultiplier, _finalFare)
            : Ride.Create(_riderId, _rideType, _regionId,
                _pickupLat, _pickupLng,
                _destLat, _destLng,
                _baseFare, _surgeMultiplier, _finalFare, _surgeReason);
    }
}
