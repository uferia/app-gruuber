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

        services.AddScoped<CreateOrderHandler>();
        services.AddScoped<TransitionOrderHandler>();
        services.AddScoped<GetOrderHandler>();
        services.AddScoped<GetOrderItemsHandler>();

        return services;
    }
}
