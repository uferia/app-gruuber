using Gruuber.Tracking.Application;
using Gruuber.Tracking.Application.Commands;
using Gruuber.Tracking.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gruuber.Tracking;

public static class TrackingModule
{
    public static IServiceCollection AddTrackingModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<TrackingDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("TrackingDb")
                ?? configuration.GetConnectionString("Default")));

        services.AddScoped<IGeoService, RedisGeoService>();
        services.AddScoped<UpdateDriverLocationHandler>();

        return services;
    }
}
