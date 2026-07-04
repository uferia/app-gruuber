using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gruuber.Restaurants;

public static class RestaurantsModule
{
    public static IServiceCollection AddRestaurantsModule(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}
