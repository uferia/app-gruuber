using Gruuber.Auth.Domain;
using Gruuber.Auth.Infrastructure;
using Gruuber.Tracking.Application;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Api.Infrastructure;

public static class DevDataSeeder
{
    // Fixed GUIDs so they're predictable across restarts
    public static readonly Guid RiderUserId     = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid DriverUserId    = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    public static readonly Guid RestaurantUserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");
    public static readonly Guid RestaurantId    = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private const string TestPassword = "Password123!";
    private const int RegionId = 1;

    // Driver seeded near Times Square, NYC
    private const double DriverLat = 40.7580;
    private const double DriverLng = -73.9855;

    public static async Task SeedAsync(IServiceProvider services, ILogger logger)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var geo = scope.ServiceProvider.GetRequiredService<IGeoService>();

        await SeedUsersAsync(db, logger);
        await SeedDriverLocationAsync(geo, logger);
    }

    private static async Task SeedUsersAsync(AuthDbContext db, ILogger logger)
    {
        var seedUsers = new[]
        {
            (RiderUserId,      "rider@test.com",      "rider"),
            (DriverUserId,     "driver@test.com",     "driver"),
            (RestaurantUserId, "restaurant@test.com", "restaurant"),
        };

        bool anyInserted = false;

        foreach (var (id, email, role) in seedUsers)
        {
            if (await db.Users.AnyAsync(u => u.Email == email))
                continue;

            var hash = BCrypt.Net.BCrypt.HashPassword(TestPassword);
            db.Users.Add(User.Create(id, email, hash, role, RegionId));
            anyInserted = true;
        }

        if (anyInserted)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("[DevSeeder] Test users created. Password for all: {Password}", TestPassword);
            logger.LogInformation("[DevSeeder] RiderUserId={RiderUserId}  DriverUserId={DriverUserId}  RestaurantUserId={RestaurantUserId}  RestaurantId={RestaurantId}",
                RiderUserId, DriverUserId, RestaurantUserId, RestaurantId);
        }
        else
        {
            logger.LogInformation("[DevSeeder] Test users already exist, skipping.");
        }
    }

    private static async Task SeedDriverLocationAsync(IGeoService geo, ILogger logger)
    {
        await geo.AddDriverLocationAsync(DriverUserId, DriverLat, DriverLng, RegionId);
        logger.LogInformation("[DevSeeder] Driver {DriverId} seeded at ({Lat}, {Lng}) in region {RegionId}",
            DriverUserId, DriverLat, DriverLng, RegionId);
    }
}
