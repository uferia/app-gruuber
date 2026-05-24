namespace Gruuber.Rides.Domain;

public class SurgePricingConfig
{
    public int RegionId { get; set; }
    public string RideType { get; set; } = string.Empty;   // "ride" | "food"
    public decimal DemandRatioThreshold { get; set; }       // e.g. 0.50
    public decimal Multiplier { get; set; }                 // e.g. 1.5
    public decimal MaxMultiplier { get; set; }              // hard cap, e.g. 3.0
    public DateTime? UpdatedAt { get; set; }
}
