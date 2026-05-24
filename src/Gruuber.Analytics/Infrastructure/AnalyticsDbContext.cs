using Gruuber.Analytics.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Analytics.Infrastructure;

public class AnalyticsDbContext : DbContext
{
    public AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : base(options) { }

    public DbSet<DriverStatsDaily> DriverStatsDaily => Set<DriverStatsDaily>();
    public DbSet<RestaurantStatsDaily> RestaurantStatsDaily => Set<RestaurantStatsDaily>();
    public DbSet<MenuItemStatsDaily> MenuItemStatsDaily => Set<MenuItemStatsDaily>();
    public DbSet<AdminStatsDaily> AdminStatsDaily => Set<AdminStatsDaily>();
    public DbSet<AnalyticsExportJob> ExportJobs => Set<AnalyticsExportJob>();
    public DbSet<ProcessedAnalyticsEvent> ProcessedEvents => Set<ProcessedAnalyticsEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DriverStatsDaily>(e =>
        {
            e.ToTable("driver_stats_daily");
            e.HasKey(x => new { x.DriverId, x.StatDate });
            e.Property(x => x.GrossEarnings).HasColumnType("numeric(12,2)");
            e.Property(x => x.BonusEarnings).HasColumnType("numeric(12,2)");
            e.Property(x => x.PayoutAmount).HasColumnType("numeric(12,2)");
            e.Property(x => x.AvgRating).HasColumnType("numeric(3,2)");
            e.Property(x => x.AcceptanceRate).HasColumnType("numeric(4,3)");
        });

        modelBuilder.Entity<RestaurantStatsDaily>(e =>
        {
            e.ToTable("restaurant_stats_daily");
            e.HasKey(x => new { x.RestaurantId, x.StatDate });
            e.Property(x => x.GrossRevenue).HasColumnType("numeric(12,2)");
            e.Property(x => x.AvgRating).HasColumnType("numeric(3,2)");
        });

        modelBuilder.Entity<MenuItemStatsDaily>(e =>
        {
            e.ToTable("menu_item_stats_daily");
            e.HasKey(x => new { x.RestaurantId, x.ItemName, x.StatDate });
            e.Property(x => x.Revenue).HasColumnType("numeric(12,2)");
        });

        modelBuilder.Entity<AdminStatsDaily>(e =>
        {
            e.ToTable("admin_stats_daily");
            e.HasKey(x => new { x.RegionId, x.StatDate });
            e.Property(x => x.GrossPlatformRevenue).HasColumnType("numeric(14,2)");
        });

        modelBuilder.Entity<AnalyticsExportJob>(e =>
        {
            e.ToTable("analytics_export_jobs");
            e.HasKey(x => x.JobId);
            e.HasIndex(x => new { x.OwnerId, x.Status });
        });

        modelBuilder.Entity<ProcessedAnalyticsEvent>(e =>
        {
            e.ToTable("processed_analytics_events");
            e.HasKey(x => x.EventId);
        });
    }
}
