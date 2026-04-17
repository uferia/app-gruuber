using Gruuber.Api.Hubs;
using Gruuber.Api.Infrastructure;
using Gruuber.Api.Infrastructure.Kafka;using Gruuber.Auth;
using Gruuber.Orders;
using Gruuber.Payments;
using Gruuber.Rides;
using Gruuber.Rides.Application.Commands;
using Gruuber.SharedKernel.Messaging;
using Gruuber.Tracking;
using Gruuber.Tracking.Application;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Structured logging
builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext()
       .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter()));

// Redis
var redisConn = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConn));

// Kafka infrastructure
builder.Services.AddSingleton<IExponentialBackoff, ExponentialBackoff>();
builder.Services.AddSingleton<IKafkaProducer, KafkaProducer>();
builder.Services.AddHostedService<OutboxWorker>();

// Modules
builder.Services.AddAuthModule(builder.Configuration);
builder.Services.AddRidesModule(builder.Configuration);
builder.Services.AddOrdersModule(builder.Configuration);
builder.Services.AddPaymentsModule(builder.Configuration);
builder.Services.AddTrackingModule(builder.Configuration);

// SignalR broadcaster (bridges Tracking module to SignalR hub)
builder.Services.AddScoped<ILocationBroadcaster, SignalRLocationBroadcaster>();

// Driver scoring (bridges Rides module with Tracking/GEO)
builder.Services.AddScoped<IDriverScoringService, DefaultDriverScoringService>();

// SignalR
builder.Services.AddSignalR();

// YARP reverse proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Health checks
builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("Default") ?? string.Empty,
        name: "postgres",
        tags: new[] { "ready" })
    .AddRedis(
        redisConn,
        name: "redis",
        tags: new[] { "ready" });

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Gruuber API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter your JWT token"
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    var seederLogger = app.Services.GetRequiredService<ILogger<Program>>();
    await DevDataSeeder.SeedAsync(app.Services, seederLogger);
}

app.UseSerilogRequestLogging();

app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Gruuber API v1"));

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true
});

app.MapHealthChecks("/health/readiness", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

app.MapControllers();
app.MapHub<LocationHub>("/hubs/location");
app.MapReverseProxy();

app.Run();
