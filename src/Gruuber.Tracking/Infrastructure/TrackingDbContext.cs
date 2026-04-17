using Microsoft.EntityFrameworkCore;

namespace Gruuber.Tracking.Infrastructure;

public class TrackingDbContext : DbContext
{
    public TrackingDbContext(DbContextOptions<TrackingDbContext> options) : base(options) { }

    public DbSet<RideViewEntry> RideViews => Set<RideViewEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RideViewEntry>(e =>
        {
            e.ToTable("ride_views");
            e.HasKey(x => x.RideId);
            e.HasIndex(x => x.DriverId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.RegionId);
        });
    }
}
