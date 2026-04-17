namespace Gruuber.Tracking.Application.Commands;

public record UpdateDriverLocationCommand(Guid DriverId, double Lat, double Lng, int RegionId, Guid? ActiveRideId);
