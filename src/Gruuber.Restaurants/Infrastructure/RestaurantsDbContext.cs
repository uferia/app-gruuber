using Gruuber.Restaurants.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Restaurants.Infrastructure;

public class RestaurantsDbContext : DbContext
{
    public DbSet<Restaurant> Restaurants => Set<Restaurant>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();

    public RestaurantsDbContext(DbContextOptions<RestaurantsDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Restaurant>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.CuisineType).HasMaxLength(100);
            e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.RejectionReason).HasMaxLength(500);
            e.Property(x => x.ApprovalStatus).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => x.OwnerUserId).IsUnique();
            e.HasIndex(x => new { x.RegionId, x.ApprovalStatus });
        });

        modelBuilder.Entity<MenuItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Category).HasMaxLength(100);
            e.Property(x => x.Price).HasPrecision(10, 2);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.HasIndex(x => x.RestaurantId);
        });
    }
}
