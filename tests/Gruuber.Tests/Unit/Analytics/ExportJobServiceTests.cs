using Gruuber.Analytics.Application;
using Gruuber.Analytics.Domain;
using Gruuber.Analytics.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gruuber.Tests.Unit.Analytics;

public class ExportJobServiceTests
{
    private static AnalyticsDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AnalyticsDbContext(opts);
    }

    [Fact]
    public async Task EnqueueExport_CreatesJobWithPendingStatus()
    {
        await using var db = CreateInMemoryDb();
        var svc = new ExportJobService(db, NullLogger<ExportJobService>.Instance);

        var jobId = await svc.EnqueueAsync(Guid.NewGuid(), "driver", "csv",
            DateOnly.Parse("2026-01-01"), DateOnly.Parse("2026-01-31"), CancellationToken.None);

        var job = await db.ExportJobs.FindAsync(jobId);
        Assert.NotNull(job);
        Assert.Equal("pending", job!.Status);
    }

    [Fact]
    public async Task GetJobStatus_ReturnsNull_WhenJobDoesNotExist()
    {
        await using var db = CreateInMemoryDb();
        var svc = new ExportJobService(db, NullLogger<ExportJobService>.Instance);

        var result = await svc.GetStatusAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetJobStatus_ReturnsNull_WhenOwnerMismatch()
    {
        await using var db = CreateInMemoryDb();
        var svc = new ExportJobService(db, NullLogger<ExportJobService>.Instance);

        var ownerId = Guid.NewGuid();
        db.ExportJobs.Add(new AnalyticsExportJob
        {
            OwnerId = ownerId, Role = "driver", Format = "csv",
            FromDate = DateOnly.Parse("2026-01-01"), ToDate = DateOnly.Parse("2026-01-31")
        });
        await db.SaveChangesAsync();

        var job = await db.ExportJobs.SingleAsync();
        var result = await svc.GetStatusAsync(job.JobId, Guid.NewGuid() /* different owner */, CancellationToken.None);
        Assert.Null(result); // unauthorized — returns null → controller returns 403
    }
}
