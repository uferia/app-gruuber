namespace Gruuber.Tracking.Domain;

public record DriverLocation(
    Guid DriverId,
    double Lat,
    double Lng,
    int RegionId,
    DateTime RecordedAt);
