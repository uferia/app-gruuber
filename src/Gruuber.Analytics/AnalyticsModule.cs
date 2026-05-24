using Gruuber.Analytics.Application;
using Gruuber.Analytics.Application.Queries;
using Gruuber.Analytics.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gruuber.Analytics;

public static class AnalyticsModule
{
    public static IServiceCollection AddAnalyticsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AnalyticsDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("AnalyticsDb")
                ?? configuration.GetConnectionString("Default")));

        services.AddScoped<DriverDashboardQueryHandler>();
        services.AddScoped<RestaurantDashboardQueryHandler>();
        services.AddScoped<AdminDashboardQueryHandler>();
        services.AddScoped<ExportJobService>();
        services.AddHostedService<AnalyticsConsumerService>();

        return services;
    }
}
