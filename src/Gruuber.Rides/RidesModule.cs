using Gruuber.Rides.Application;
using Gruuber.Rides.Application.Commands;
using Gruuber.Rides.Application.Queries;
using Gruuber.Rides.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gruuber.Rides;

public static class RidesModule
{
    public static IServiceCollection AddRidesModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<RidesDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("RidesDb")
                ?? configuration.GetConnectionString("Default")));

        services.AddScoped<RequestRideHandler>();
        services.AddScoped<MatchDriverHandler>();
        services.AddScoped<GetRideStatusHandler>();
        services.AddScoped<TransitionRideHandler>();
        services.AddScoped<AcceptSoloUpgradeHandler>();
        services.AddHostedService<PoolMatcherService>();
        services.AddHostedService<PoolTimeoutWorker>();

        return services;
    }
}
