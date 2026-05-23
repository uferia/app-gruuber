using System.Text.Json;
using Confluent.Kafka;
using Gruuber.SharedKernel.Messaging;
using Gruuber.Tracking.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gruuber.Tracking.Application;

public class RideViewConsumer : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IKafkaProducer _producer;
    private readonly ILogger<RideViewConsumer> _logger;

    public RideViewConsumer(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IKafkaProducer producer,
        ILogger<RideViewConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _producer = producer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        var groupId = _configuration["Kafka:ConsumerGroupId"] ?? "gruuber-tracking";
        var regions = _configuration.GetSection("Kafka:RideRegions").Get<int[]>() ?? [1];

        var topics = regions.Select(r => $"ride-events-{r}").ToList();

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(topics);

        _logger.LogInformation("RideViewConsumer subscribed to topics: {Topics}", string.Join(", ", topics));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);
                if (result?.Message?.Value is null) continue;

                var retryCount = 0;
                var success = false;

                while (retryCount <= 5 && !stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await ProcessMessageAsync(result.Message.Value, stoppingToken);
                        consumer.Commit(result);
                        success = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        _logger.LogWarning(ex, "RideViewConsumer failed processing (attempt {Attempt})", retryCount);
                        if (retryCount <= 5)
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount - 1)), stoppingToken);
                    }
                }

                if (!success)
                {
                    _logger.LogError("RideViewConsumer routing message to DLQ after 5 failures");
                    await _producer.PublishAsync(
                        $"{result.Topic}-dlq",
                        result.Message.Key,
                        result.Message.Value,
                        stoppingToken);
                    consumer.Commit(result);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RideViewConsumer unexpected error");
                await Task.Delay(1000, stoppingToken);
            }
        }

        consumer.Close();
    }

    private async Task ProcessMessageAsync(string payload, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        if (!root.TryGetProperty("EventName", out var eventNameEl)) return;
        var eventName = eventNameEl.GetString();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackingDbContext>();

        if (eventName == "driver_matched")
        {
            var rideId = root.GetProperty("RideId").GetGuid();
            var driverId = root.GetProperty("DriverId").GetGuid();
            var regionId = root.GetProperty("RegionId").GetInt32();

            var existing = await db.RideViews.FindAsync(new object[] { rideId }, cancellationToken);
            if (existing is null)
            {
                db.RideViews.Add(new RideViewEntry
                {
                    RideId = rideId,
                    DriverId = driverId,
                    Status = "matched",
                    RegionId = regionId,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.DriverId = driverId;
                existing.Status = "matched";
                existing.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("RideView upserted for ride {RideId} driver {DriverId}", rideId, driverId);
        }
        else if (eventName == "ride_status_changed")
        {
            var rideId = root.GetProperty("RideId").GetGuid();
            var newStatus = root.GetProperty("NewStatus").GetString();

            var existing = await db.RideViews.FindAsync(new object[] { rideId }, cancellationToken);
            if (existing is not null)
            {
                existing.Status = newStatus!;
                existing.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("RideView status updated for ride {RideId} to {Status}", rideId, newStatus);
            }
        }
    }
}
