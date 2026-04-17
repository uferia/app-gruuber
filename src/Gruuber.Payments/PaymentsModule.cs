using Gruuber.Payments.Application;
using Gruuber.Payments.Application.Commands;
using Gruuber.Payments.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gruuber.Payments;

public static class PaymentsModule
{
    public static IServiceCollection AddPaymentsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<PaymentsDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("PaymentsDb")
                ?? configuration.GetConnectionString("Default")));

        services.AddScoped<InitiatePaymentHandler>();
        services.AddScoped<ConfirmPaymentHandler>();
        services.AddScoped<FailPaymentHandler>();
        services.AddHostedService<PaymentTimeoutWorker>();

        return services;
    }
}
