using Gruuber.Api.Hubs;
using Gruuber.Api.Infrastructure;
using Gruuber.Api.Infrastructure.Kafka;
using Gruuber.Auth;
using Gruuber.Orders;
using Gruuber.Payments;
using Gruuber.Rides;
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

var app = builder.Build();

app.UseSerilogRequestLogging();

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
