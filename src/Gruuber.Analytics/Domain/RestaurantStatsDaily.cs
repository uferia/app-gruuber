namespace Gruuber.Analytics.Domain;

public class RestaurantStatsDaily
{
    public Guid RestaurantId { get; set; }
    public int RegionId { get; set; }
    public DateOnly StatDate { get; set; }
    public int OrdersReceived { get; set; }
    public int OrdersCompleted { get; set; }
    public int OrdersCancelled { get; set; }
    public decimal GrossRevenue { get; set; }
    public int AvgPrepTimeSecs { get; set; }
    public decimal AvgRating { get; set; }
}
