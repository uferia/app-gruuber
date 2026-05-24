namespace Gruuber.Analytics.Domain;

public class AdminStatsDaily
{
    public int RegionId { get; set; }
    public DateOnly StatDate { get; set; }
    public int TotalRides { get; set; }
    public int TotalPoolRides { get; set; }
    public int TotalOrders { get; set; }
    public decimal GrossPlatformRevenue { get; set; }
    public int ActiveDrivers { get; set; }
    public int ActiveRestaurants { get; set; }
}
