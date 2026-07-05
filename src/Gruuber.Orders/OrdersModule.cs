using Gruuber.Orders.Application.Commands;
using Gruuber.Orders.Application.Queries;
using Gruuber.Orders.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gruuber.Orders;

public static class OrdersModule
{
    public static IServiceCollection AddOrdersModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<OrdersDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("OrdersDb")
                ?? configuration.GetConnectionString("Default")));

        var deliveryFee = decimal.TryParse(configuration["Orders:DeliveryFee"], out var fee) ? fee : 2.50m;
        services.AddSingleton(new Application.OrderPricingOptions(deliveryFee));

        services.AddScoped<CreateOrderHandler>();
        services.AddScoped<TransitionOrderHandler>();
        services.AddScoped<GetOrderHandler>();
        services.AddScoped<GetOrderItemsHandler>();
        services.AddScoped<GetRestaurantOrdersHandler>();

        return services;
    }
}
