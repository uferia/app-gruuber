using Gruuber.Restaurants.Application.Commands;
using Gruuber.Restaurants.Application.Queries;
using Gruuber.Restaurants.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gruuber.Restaurants;

public static class RestaurantsModule
{
    public static IServiceCollection AddRestaurantsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<RestaurantsDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("RestaurantsDb")
                ?? configuration.GetConnectionString("Default")));

        services.AddScoped<RegisterRestaurantHandler>();
        services.AddScoped<RestaurantQueryHandler>();

        return services;
    }
}
