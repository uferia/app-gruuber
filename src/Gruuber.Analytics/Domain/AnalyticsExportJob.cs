namespace Gruuber.Analytics.Domain;

public class AnalyticsExportJob
{
    public Guid JobId { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public string Role { get; set; } = string.Empty;       // 'driver' | 'restaurant' | 'admin'
    public string Format { get; set; } = string.Empty;     // 'csv' | 'pdf'
    public string Status { get; set; } = "pending";        // pending | processing | completed | failed
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public string? DownloadUrl { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
