using Gruuber.Chat.Application;
using Gruuber.Chat.Application.Queries;
using Gruuber.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gruuber.Chat;

public static class ChatModule
{
    public static IServiceCollection AddChatModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ChatDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("ChatDb")
                ?? configuration.GetConnectionString("Default")));

        services.AddScoped<ChatQueryHandler>();
        services.AddHostedService<ChatThreadConsumer>();
        services.AddHostedService<ChatThreadClosureWorker>();

        return services;
    }
}
