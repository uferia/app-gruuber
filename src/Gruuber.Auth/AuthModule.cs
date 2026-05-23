using System.Text;
using Gruuber.Auth.Application;
using Gruuber.Auth.Application.Commands;
using Gruuber.Auth.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Gruuber.Auth;

public static class AuthModule
{
    public static IServiceCollection AddAuthModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AuthDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("AuthDb")
                ?? configuration.GetConnectionString("Default")));

        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<RefreshTokenHandler>();
        services.AddScoped<RegisterHandler>();

        var jwtSecret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
        if (jwtSecret.Length < 32)
            throw new InvalidOperationException("Jwt:Secret must be at least 32 characters long for HMAC-SHA256 security.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = configuration["Jwt:Issuer"],
                    ValidAudience = configuration["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("rider", p => p.RequireRole("rider"));
            options.AddPolicy("driver", p => p.RequireRole("driver"));
            options.AddPolicy("restaurant", p => p.RequireRole("restaurant"));
        });

        return services;
    }
}
