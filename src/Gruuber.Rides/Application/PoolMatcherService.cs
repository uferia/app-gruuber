using System.Text.Json;
using Confluent.Kafka;
using Gruuber.Rides.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Gruuber.Rides.Application;

/// <summary>
/// Kafka consumer that listens for ride_pool_queued events and attempts to match
/// compatible pool riders within the same region.
/// </summary>
public class PoolMatcherService : BackgroundService
{
    private readonly PoolMatchCoordinator _coordinator;
    private readonly IConfiguration? _configuration;
    private readonly ILogger<PoolMatcherService> _logger;

    public PoolMatcherService(IServiceScopeFactory scopeFactory, IConnectionMultiplexer redis,
        IConfiguration configuration, ILogger<PoolMatcherService> logger)
    {
        _coordinator = new PoolMatchCoordinator(scopeFactory, redis, new RideOutboxFactory(), logger);
        _configuration = configuration;
        _logger = logger;
    }

    internal PoolMatcherService(RidesDbContext db, IConnectionMultiplexer redis, ILogger<PoolMatcherService> logger)
    {
        _coordinator = new PoolMatchCoordinator(db, redis, new RideOutboxFactory(), logger);
        _configuration = null;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_configuration is null) return;

        var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        var groupId = _configuration["Kafka:PoolMatcherGroupId"] ?? "gruuber-pool-matcher";
        var regions = _configuration.GetSection("Kafka:RideRegions").Get<int[]>() ?? [1];

        var topics = regions.Select(r => $"ride-events-{r}").ToList();
        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(topics);

        _logger.LogInformation("PoolMatcherService subscribed to: {Topics}", string.Join(", ", topics));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result = null;
                try
                {
                    result = consumer.Consume(stoppingToken);
                    if (result?.Message?.Value is null) continue;

                    using var doc = JsonDocument.Parse(result.Message.Value);
                    if (!doc.RootElement.TryGetProperty("EventName", out var en) ||
                        en.GetString() != "ride_pool_queued") { consumer.Commit(result); continue; }

                    var regionId = doc.RootElement.GetProperty("RegionId").GetInt32();
                    await _coordinator.TryMatchRidesAsync(regionId, stoppingToken);
                    consumer.Commit(result);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "PoolMatcherService error processing message");
                    if (result is not null) consumer.Commit(result);
                    try { await Task.Delay(1000, stoppingToken); } catch (OperationCanceledException) { break; }
                }
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    internal Task<bool> TryMatchRidesAsync(int regionId, CancellationToken ct)
        => _coordinator.TryMatchRidesAsync(regionId, ct);
}