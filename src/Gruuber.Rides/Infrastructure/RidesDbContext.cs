using Gruuber.Rides.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Rides.Infrastructure;

public class RidesDbContext : DbContext
{
    public RidesDbContext(DbContextOptions<RidesDbContext> options) : base(options) { }

    public DbSet<Ride> Rides => Set<Ride>();

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
    }
}
