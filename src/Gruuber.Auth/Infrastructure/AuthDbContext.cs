using Gruuber.Auth.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Auth.Infrastructure;

public class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<RiderProfile> RiderProfiles => Set<RiderProfile>();
    public DbSet<DriverProfile> DriverProfiles => Set<DriverProfile>();
    public DbSet<DriverVehicle> DriverVehicles => Set<DriverVehicle>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).IsRequired().HasMaxLength(256);
            e.Property(x => x.Role).IsRequired().HasMaxLength(32);
            e.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).IsRequired();
            e.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<RiderProfile>(e =>
        {
            e.ToTable("rider_profiles");
            e.HasKey(x => x.Id);
            e.Property(x => x.FirstName).IsRequired().HasMaxLength(100);
            e.Property(x => x.LastName).IsRequired().HasMaxLength(100);
            e.Property(x => x.PhoneNumber).IsRequired().HasMaxLength(20);
            e.Property(x => x.ProfilePhotoUrl).HasMaxLength(2048);
            e.HasIndex(x => x.UserId).IsUnique();
        });

        modelBuilder.Entity<DriverProfile>(e =>
        {
            e.ToTable("driver_profiles");
            e.HasKey(x => x.Id);
            e.Property(x => x.FirstName).IsRequired().HasMaxLength(100);
            e.Property(x => x.LastName).IsRequired().HasMaxLength(100);
            e.Property(x => x.PhoneNumber).IsRequired().HasMaxLength(20);
            e.Property(x => x.ProfilePhotoUrl).HasMaxLength(2048);
            e.Property(x => x.LicenseNumber).IsRequired().HasMaxLength(50);
            e.Property(x => x.DriverLicenseDocumentUrl).IsRequired();
            e.Property(x => x.MotorVehicleRegistrationUrl).IsRequired();
            e.Property(x => x.InsurancePolicyUrl).IsRequired();
            e.Property(x => x.NbiClearanceUrl).IsRequired();
            e.Property(x => x.ApprovalStatus)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(32);
            e.Property(x => x.RejectionReason).HasMaxLength(1000);
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasIndex(x => x.ApprovalStatus);
        });

        modelBuilder.Entity<DriverVehicle>(e =>
        {
            e.ToTable("driver_vehicles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Make).IsRequired().HasMaxLength(100);
            e.Property(x => x.Model).IsRequired().HasMaxLength(100);
            e.Property(x => x.Color).IsRequired().HasMaxLength(50);
            e.Property(x => x.LicensePlate).IsRequired().HasMaxLength(20);
            e.Property(x => x.VehicleType)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(32);
            e.HasIndex(x => x.DriverProfileId);
        });
    }
}
