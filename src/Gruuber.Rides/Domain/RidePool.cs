namespace Gruuber.Rides.Domain;

public class PoolRegionRate
{
    public int RegionId { get; set; }
    public decimal DiscountPct { get; set; }           // e.g. 0.20 = 20% off
    public int MatchTimeoutSecs { get; set; } = 120;
    public decimal MaxDetourKm { get; set; } = 2.0m;
    public DateTime? UpdatedAt { get; set; }
}
