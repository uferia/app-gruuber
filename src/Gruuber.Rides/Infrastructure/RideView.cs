namespace Gruuber.Rides.Infrastructure;

public class RideView
{
    public Guid RideId { get; set; }
    public Guid? DriverId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lng { get; set; }
    public int RegionId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? RideType { get; set; }
    public int? PoolSlot { get; set; }
}
