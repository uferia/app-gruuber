namespace Gruuber.Tracking.Infrastructure;

public class RideViewEntry
{
    public Guid RideId { get; set; }
    public Guid? DriverId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lng { get; set; }
    public int RegionId { get; set; }
    public DateTime UpdatedAt { get; set; }
}
