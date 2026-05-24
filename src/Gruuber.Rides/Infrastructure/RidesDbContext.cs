using Gruuber.Rides.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Rides.Infrastructure;

public class RidesDbContext : DbContext
{
    public RidesDbContext(DbContextOptions<RidesDbContext> options) : base(options) { }

    public DbSet<Ride> Rides => Set<Ride>();
    public DbSet<SurgePricingConfig> SurgeConfigs => Set<SurgePricingConfig>();
    public DbSet<SurgeTimeRule> SurgeTimeRules => Set<SurgeTimeRule>();
    public DbSet<PoolRegionRate> PoolRegionRates => Set<PoolRegionRate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ride>(e =>
        {
            e.ToTable("rides");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().IsRequired();
            e.Property(x => x.RideType).HasMaxLength(64);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.Status, x.RegionId });
            e.Property(x => x.DestLat);
            e.Property(x => x.DestLng);
            e.Property(x => x.BaseFare).HasColumnType("numeric(10,2)");
            e.Property(x => x.SurgeMultiplier).HasColumnType("numeric(6,2)").HasDefaultValue(1.0m);
            e.Property(x => x.FinalFare).HasColumnType("numeric(10,2)");
            e.Property(x => x.SurgeReason).HasMaxLength(32);
            e.Property(x => x.PoolTripId);
            e.Property(x => x.PoolSlot);
        });

        modelBuilder.Entity<SurgePricingConfig>(e =>
        {
            e.ToTable("surge_config");
            e.HasKey(x => new { x.RegionId, x.RideType, x.DemandRatioThreshold });
            e.Property(x => x.Multiplier).HasColumnType("numeric(6,2)");
            e.Property(x => x.MaxMultiplier).HasColumnType("numeric(6,2)");
            e.Property(x => x.DemandRatioThreshold).HasColumnType("numeric(4,3)");
        });

        modelBuilder.Entity<SurgeTimeRule>(e =>
        {
            e.ToTable("surge_time_rules");
            e.HasKey(x => x.Id);
            e.Property(x => x.Multiplier).HasColumnType("numeric(6,2)");
            e.Property(x => x.StartTime).HasColumnType("time");
            e.Property(x => x.EndTime).HasColumnType("time");
        });

        // Outbox table
        modelBuilder.Entity<RideOutboxEntry>(e =>
        {
            e.ToTable("ride_outbox");
            e.HasKey(x => x.Id);
            e.Property(x => x.Payload).HasColumnType("jsonb");
        });

        // Read model (populated by Kafka consumer)
        modelBuilder.Entity<RideView>(e =>
        {
            e.ToTable("ride_views");
            e.HasKey(x => x.RideId);
            e.HasIndex(x => new { x.DriverId, x.Status });
        });

        modelBuilder.Entity<PoolRegionRate>(e =>
        {
            e.ToTable("pool_region_rates");
            e.HasKey(x => x.RegionId);
            e.Property(x => x.RegionId).ValueGeneratedNever();
            e.Property(x => x.DiscountPct).HasColumnType("numeric(4,3)");
            e.Property(x => x.MaxDetourKm).HasColumnType("numeric(6,2)");
        });
    }
}
