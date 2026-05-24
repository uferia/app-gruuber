namespace Gruuber.Analytics.Domain;

public class ProcessedAnalyticsEvent
{
    public Guid EventId { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
