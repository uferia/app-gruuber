using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gruuber.Rides.Infrastructure;

public class RidesDbContextFactory : IDesignTimeDbContextFactory<RidesDbContext>
{
    public RidesDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<RidesDbContext>()
            .UseNpgsql("Host=localhost;Database=gruuber;Username=gruuber;Password=gruuber")
            .Options;
        return new RidesDbContext(opts);
    }
}
