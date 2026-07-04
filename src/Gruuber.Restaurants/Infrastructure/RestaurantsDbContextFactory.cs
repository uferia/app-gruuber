using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gruuber.Restaurants.Infrastructure;

public class RestaurantsDbContextFactory : IDesignTimeDbContextFactory<RestaurantsDbContext>
{
    public RestaurantsDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<RestaurantsDbContext>()
            .UseNpgsql("Host=localhost;Database=gruuber_restaurants;Username=gruuber;Password=gruuber")
            .Options;
        return new RestaurantsDbContext(opts);
    }
}
