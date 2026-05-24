namespace Gruuber.Analytics.Domain;

public class MenuItemStatsDaily
{
    public Guid RestaurantId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public DateOnly StatDate { get; set; }
    public int UnitsSold { get; set; }
    public decimal Revenue { get; set; }
}
