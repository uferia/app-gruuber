using System.Text.Json;
using Confluent.Kafka;
using Gruuber.Chat.Domain;
using Gruuber.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gruuber.Chat.Application;

public class ChatThreadConsumer : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ChatThreadConsumer> _logger;

    public ChatThreadConsumer(IServiceScopeFactory scopeFactory, IConfiguration configuration,
        ILogger<ChatThreadConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        var groupId = _configuration["Kafka:ChatGroupId"] ?? "gruuber-chat";
        var regions = _configuration.GetSection("Kafka:RideRegions").Get<int[]>() ?? [1];

        var topics = regions.SelectMany(r => new[]
        {
            $"ride-events-{r}",
            $"order-events-{r}"
        }).ToList();

        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(topics);
        _logger.LogInformation("ChatThreadConsumer subscribed to: {Topics}", string.Join(", ", topics));

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result = null;
            var retryCount = 0;
            try
            {
                result = consumer.Consume(stoppingToken);
                if (result?.Message?.Value is null) continue;

                var success = false;
                while (retryCount <= 5 && !stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
                        var processor = new ChatEventProcessor(db, _logger);
                        await processor.ProcessAsync(result.Message.Value, stoppingToken);
                        consumer.Commit(result);
                        success = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        _logger.LogWarning(ex, "ChatThreadConsumer failed (attempt {Attempt})", retryCount);
                        if (retryCount <= 5)
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount - 1)), stoppingToken);
                    }
                }

                if (!success)
                {
                    _logger.LogError("ChatThreadConsumer routing to DLQ after 5 failures on topic {Topic}", result?.Topic);
                    consumer.Commit(result!);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ChatThreadConsumer unexpected error");
                await Task.Delay(1000, stoppingToken);
            }
        }

        consumer.Close();
    }
}

public class ChatEventProcessor
{
    private readonly ChatDbContext _db;
    private readonly ILogger _logger;
    private static readonly TimeSpan ThreadLifetime = TimeSpan.FromHours(24);

    public ChatEventProcessor(ChatDbContext db, ILogger logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ProcessAsync(string payload, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        if (!root.TryGetProperty("EventName", out var en)) return;

        switch (en.GetString())
        {
            case "ride_matched": await HandleRideMatched(root, ct); break;
            case "order_accepted": await HandleOrderAccepted(root, ct); break;
        }
    }

    private async Task HandleRideMatched(JsonElement root, CancellationToken ct)
    {
        var rideId = root.GetProperty("RideId").GetGuid();
        var riderId = root.GetProperty("RiderId").GetGuid();
        var driverId = root.GetProperty("DriverId").GetGuid();
        var regionId = root.TryGetProperty("RegionId", out var r) ? r.GetInt32() : 1;

        var existing = await _db.Threads
            .AnyAsync(t => t.ContextType == "ride" && t.ContextId == rideId, ct);
        if (existing) return;

        var thread = new ChatThread
        {
            ContextType = "ride",
            ContextId = rideId,
            RegionId = regionId,
            Status = "active",
            ClosesAt = DateTime.UtcNow.Add(ThreadLifetime)
        };
        thread.Participants.AddRange([
            new ChatParticipant { ThreadId = thread.ThreadId, UserId = riderId, DisplayName = "Your Rider", Role = "rider" },
            new ChatParticipant { ThreadId = thread.ThreadId, UserId = driverId, DisplayName = "Your Driver", Role = "driver" }
        ]);

        _db.Threads.Add(thread);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Chat thread {ThreadId} created for ride {RideId}", thread.ThreadId, rideId);
    }

    private async Task HandleOrderAccepted(JsonElement root, CancellationToken ct)
    {
        var orderId = root.GetProperty("OrderId").GetGuid();
        var riderId = root.GetProperty("RiderId").GetGuid();
        var driverId = root.GetProperty("DriverId").GetGuid();
        var restaurantId = root.GetProperty("RestaurantId").GetGuid();
        var regionId = root.TryGetProperty("RegionId", out var r) ? r.GetInt32() : 1;

        var existingCount = await _db.Threads
            .CountAsync(t => t.ContextType == "order" && t.ContextId == orderId, ct);
        if (existingCount >= 2) return;

        var closesAt = DateTime.UtcNow.Add(ThreadLifetime);

        var riderDriverThread = new ChatThread
        {
            ContextType = "order",
            ContextId = orderId,
            RegionId = regionId,
            Status = "active",
            ClosesAt = closesAt
        };
        riderDriverThread.Participants.AddRange([
            new ChatParticipant { ThreadId = riderDriverThread.ThreadId, UserId = riderId, DisplayName = "Your Rider", Role = "rider" },
            new ChatParticipant { ThreadId = riderDriverThread.ThreadId, UserId = driverId, DisplayName = "Your Driver", Role = "driver" }
        ]);

        var riderRestaurantThread = new ChatThread
        {
            ContextType = "order",
            ContextId = orderId,
            RegionId = regionId,
            Status = "active",
            ClosesAt = closesAt
        };
        riderRestaurantThread.Participants.AddRange([
            new ChatParticipant { ThreadId = riderRestaurantThread.ThreadId, UserId = riderId, DisplayName = "Your Rider", Role = "rider" },
            new ChatParticipant { ThreadId = riderRestaurantThread.ThreadId, UserId = restaurantId, DisplayName = "Restaurant Staff", Role = "restaurant" }
        ]);

        _db.Threads.AddRange(riderDriverThread, riderRestaurantThread);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Chat threads created for order {OrderId}: {Thread1}, {Thread2}",
            orderId, riderDriverThread.ThreadId, riderRestaurantThread.ThreadId);
    }
}
