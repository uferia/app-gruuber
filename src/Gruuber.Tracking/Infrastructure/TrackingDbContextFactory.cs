using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gruuber.Tracking.Infrastructure;

public class TrackingDbContextFactory : IDesignTimeDbContextFactory<TrackingDbContext>
{
    public TrackingDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<TrackingDbContext>()
            .UseNpgsql("Host=localhost;Database=gruuber;Username=gruuber;Password=gruuber")
            .Options;
        return new TrackingDbContext(opts);
    }
}
