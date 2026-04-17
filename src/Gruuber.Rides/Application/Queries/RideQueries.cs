namespace Gruuber.Rides.Application.Queries;

public record GetRideStatusQuery(Guid RideId);
public record RideStatusResponse(Guid RideId, Guid? DriverId, string DriverName, string Status, double Lat, double Lng);
