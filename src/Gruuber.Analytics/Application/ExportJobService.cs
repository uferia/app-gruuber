using Gruuber.Analytics.Domain;
using Gruuber.Analytics.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace Gruuber.Analytics.Application;

public class ExportJobService
{
    private readonly AnalyticsDbContext _db;
    private readonly ILogger<ExportJobService> _logger;
    private const int DownloadUrlTtlMinutes = 60;

    public ExportJobService(AnalyticsDbContext db, ILogger<ExportJobService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Enqueues a new export job and returns the JobId.</summary>
    public async Task<Guid> EnqueueAsync(
        Guid ownerId, string role, string format,
        DateOnly fromDate, DateOnly toDate,
        CancellationToken ct)
    {
        var job = new AnalyticsExportJob
        {
            OwnerId = ownerId,
            Role = role,
            Format = format,
            Status = "pending",
            FromDate = fromDate,
            ToDate = toDate,
            CreatedAt = DateTime.UtcNow
        };
        _db.ExportJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Export job {JobId} enqueued for owner={OwnerId} format={Format}",
            job.JobId, ownerId, format);
        return job.JobId;
    }

    /// <summary>
    /// Returns the job status for the specified owner.
    /// Returns null if not found OR if owner mismatch (caller should return 404/403).
    /// </summary>
    public async Task<ExportJobStatusResponse?> GetStatusAsync(Guid jobId, Guid callerId, CancellationToken ct)
    {
        var job = await _db.ExportJobs.FirstOrDefaultAsync(j => j.JobId == jobId, ct);
        if (job is null) return null;
        if (job.OwnerId != callerId) return null;   // auth mismatch — caller returns 403

        return new ExportJobStatusResponse(job.JobId, job.Status, job.DownloadUrl, job.ExpiresAt);
    }

    /// <summary>
    /// Processes a pending export job (called by background worker).
    /// For CSV, generates file bytes via CsvHelper. For PDF, uses QuestPDF.
    /// Sets download_url to a data URL (replace with blob storage in production).
    /// </summary>
    public async Task ProcessJobAsync(Guid jobId, CancellationToken ct)
    {
        var job = await _db.ExportJobs.FindAsync([jobId], ct);
        if (job is null || job.Status != "pending") return;

        job.Status = "processing";
        await _db.SaveChangesAsync(ct);

        try
        {
            byte[] fileBytes;
            if (job.Format == "csv")
                fileBytes = await GenerateCsvAsync(job, ct);
            else
                fileBytes = await GeneratePdfAsync(job, ct);

            // In production: upload to Azure Blob / S3, return presigned URL
            var dataUrl = $"data:application/{job.Format};base64,{Convert.ToBase64String(fileBytes)}";

            job.Status = "completed";
            job.DownloadUrl = dataUrl;
            job.ExpiresAt = DateTime.UtcNow.AddMinutes(DownloadUrlTtlMinutes);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Export job {JobId} completed for owner={OwnerId}", jobId, job.OwnerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export job {JobId} failed", jobId);
            job.Status = "failed";
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task<byte[]> GenerateCsvAsync(AnalyticsExportJob job, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms);
        using var csv = new CsvHelper.CsvWriter(writer, System.Globalization.CultureInfo.InvariantCulture);

        if (job.Role == "driver")
        {
            var rows = await _db.DriverStatsDaily
                .Where(x => x.DriverId == job.OwnerId
                            && x.StatDate >= job.FromDate && x.StatDate <= job.ToDate)
                .OrderBy(x => x.StatDate)
                .ToListAsync(ct);
            csv.WriteRecords(rows);
        }
        else if (job.Role == "restaurant")
        {
            var rows = await _db.RestaurantStatsDaily
                .Where(x => x.RestaurantId == job.OwnerId
                            && x.StatDate >= job.FromDate && x.StatDate <= job.ToDate)
                .OrderBy(x => x.StatDate)
                .ToListAsync(ct);
            csv.WriteRecords(rows);
        }

        await writer.FlushAsync(ct);
        return ms.ToArray();
    }

    private Task<byte[]> GeneratePdfAsync(AnalyticsExportJob job, CancellationToken ct)
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        var pdfBytes = QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Content().Column(col =>
                {
                    col.Item().Text("Gruuber Analytics Report").FontSize(20).Bold();
                    col.Item().Text($"Role: {job.Role} | Period: {job.FromDate} – {job.ToDate}");
                    col.Item().Text($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
                    col.Item().Text("(Full data available in CSV export)");
                });
            });
        }).GeneratePdf();

        return Task.FromResult(pdfBytes);
    }
}

public record ExportJobStatusResponse(Guid JobId, string Status, string? DownloadUrl, DateTime? ExpiresAt);
