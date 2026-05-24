namespace Gruuber.Analytics.Domain;

public class DriverStatsDaily
{
    public Guid DriverId { get; set; }
    public int RegionId { get; set; }
    public DateOnly StatDate { get; set; }
    public int TripsCompleted { get; set; }
    public int TripsCancelled { get; set; }
    public int PoolTrips { get; set; }
    public decimal GrossEarnings { get; set; }
    public decimal BonusEarnings { get; set; }
    public decimal PayoutAmount { get; set; }
    public decimal AvgRating { get; set; }
    public decimal AcceptanceRate { get; set; }  // 0.0–1.0
    public int OnlineMinutes { get; set; }
}
