namespace Gruuber.Rides.Domain;

public class SurgeTimeRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int RegionId { get; set; }
    public string RideType { get; set; } = string.Empty;
    public int? DayOfWeek { get; set; }    // 0=Sun…6=Sat; null=every day
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public decimal Multiplier { get; set; }
    public bool IsActive { get; set; } = true;
    public string? TimeZoneId { get; set; }  // null = UTC; e.g., "America/New_York"
}
