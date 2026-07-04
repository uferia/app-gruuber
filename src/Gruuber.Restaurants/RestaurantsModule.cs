using Gruuber.Restaurants.Application.Commands;
using Gruuber.Restaurants.Application.Queries;
using Gruuber.Restaurants.Infrastructure;
using Gruuber.SharedKernel.Catalog;
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
        services.AddScoped<UpdateRestaurantHandler>();
        services.AddScoped<SetRestaurantOpenHandler>();
        services.AddScoped<AddMenuItemHandler>();
        services.AddScoped<UpdateMenuItemHandler>();
        services.AddScoped<DeleteMenuItemHandler>();
        services.AddScoped<ApproveRestaurantHandler>();
        services.AddScoped<RejectRestaurantHandler>();
        services.AddScoped<IRestaurantCatalogReader, RestaurantCatalogReader>();

        return services;
    }
}
