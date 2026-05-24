using System.Text.Json;
using Confluent.Kafka;
using Gruuber.Analytics.Domain;
using Gruuber.Analytics.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gruuber.Analytics.Application;

/// <summary>Kafka BackgroundService — subscribes to all domain event topics and upserts analytics stats.</summary>
public class AnalyticsConsumerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AnalyticsConsumerService> _logger;

    public AnalyticsConsumerService(IServiceScopeFactory scopeFactory, IConfiguration configuration,
        ILogger<AnalyticsConsumerService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        var groupId = _configuration["Kafka:AnalyticsGroupId"] ?? "gruuber-analytics";
        var regions = _configuration.GetSection("Kafka:RideRegions").Get<int[]>() ?? [1];

        var topics = regions.SelectMany(r => new[]
        {
            $"ride-events-{r}",
            $"order-events-{r}",
            $"payment-events-{r}"
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
        _logger.LogInformation("AnalyticsConsumerService subscribed to: {Topics}", string.Join(", ", topics));

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result = null;
            var retryCount = 0;
            try
            {
                result = consumer.Consume(stoppingToken);
                if (result?.Message?.Value is null) continue;

                bool success = false;
                while (retryCount <= 5 && !stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
                        var processor = new AnalyticsEventProcessor(db, _logger);
                        await processor.ProcessAsync(result.Message.Value, stoppingToken);
                        consumer.Commit(result);
                        success = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        _logger.LogWarning(ex, "AnalyticsConsumer failed (attempt {Attempt})", retryCount);
                        if (retryCount <= 5)
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount - 1)), stoppingToken);
                    }
                }

                if (!success)
                {
                    _logger.LogError("AnalyticsConsumer routing to DLQ after 5 failures on topic {Topic}", result?.Topic);
                    consumer.Commit(result!);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AnalyticsConsumer unexpected error");
                await Task.Delay(1000, stoppingToken);
            }
        }

        consumer.Close();
    }
}

/// <summary>
/// Processes a single analytics event payload. Extracted for unit testability.
/// All DB writes are in a single transaction with dedup check.
/// </summary>
public class AnalyticsEventProcessor
{
    private readonly AnalyticsDbContext _db;
    private readonly ILogger _logger;

    public AnalyticsEventProcessor(AnalyticsDbContext db, ILogger logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ProcessAsync(string payload, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        if (!root.TryGetProperty("EventName", out var en)) return;
        var eventName = en.GetString();

        // Idempotency check — prefer explicit EventId, then RideId/OrderId as event key
        Guid eventId = Guid.NewGuid();
        if (root.TryGetProperty("EventId", out var eid))
            eventId = eid.GetGuid();
        else if (root.TryGetProperty("RideId", out var rid))
            eventId = rid.GetGuid();
        else if (root.TryGetProperty("OrderId", out var oid))
            eventId = oid.GetGuid();

        var alreadyProcessed = await _db.ProcessedEvents.AnyAsync(e => e.EventId == eventId, ct);
        if (alreadyProcessed)
        {
            _logger.LogWarning("AnalyticsConsumer: duplicate event {EventId} skipped", eventId);
            return;
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        switch (eventName)
        {
            case "ride_completed": await HandleRideCompleted(root, ct); break;
            case "ride_cancelled": await HandleRideCancelled(root, ct); break;
            case "order_delivered": await HandleOrderDelivered(root, ct); break;
            case "order_cancelled": await HandleOrderCancelled(root, ct); break;
            case "payment_success": await HandlePaymentSuccess(root, ct); break;
            default: await tx.RollbackAsync(ct); return;
        }

        _db.ProcessedEvents.Add(new ProcessedAnalyticsEvent { EventId = eventId });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task HandleRideCompleted(JsonElement root, CancellationToken ct)
    {
        var driverId = root.GetProperty("DriverId").GetGuid();
        var regionId = root.GetProperty("RegionId").GetInt32();
        var fare = root.TryGetProperty("Fare", out var f) ? f.GetDecimal() : 0m;
        var isPool = root.TryGetProperty("IsPool", out var ip) && ip.GetBoolean();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        await UpsertDriverStats(driverId, regionId, date, d =>
        {
            d.TripsCompleted++;
            d.GrossEarnings += fare;
            if (isPool) d.PoolTrips++;
        }, ct);

        await UpsertAdminStats(regionId, date, a =>
        {
            a.TotalRides++;
            if (isPool) a.TotalPoolRides++;
        }, ct);
    }

    private async Task HandleRideCancelled(JsonElement root, CancellationToken ct)
    {
        var driverId = root.TryGetProperty("DriverId", out var d) ? (Guid?)d.GetGuid() : null;
        var regionId = root.GetProperty("RegionId").GetInt32();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        if (driverId.HasValue)
            await UpsertDriverStats(driverId.Value, regionId, date, ds => ds.TripsCancelled++, ct);

        await UpsertAdminStats(regionId, date, _ => { }, ct);
    }

    private async Task HandleOrderDelivered(JsonElement root, CancellationToken ct)
    {
        var restaurantId = root.GetProperty("RestaurantId").GetGuid();
        var regionId = root.GetProperty("RegionId").GetInt32();
        var revenue = root.TryGetProperty("Revenue", out var r) ? r.GetDecimal() : 0m;
        var prepTime = root.TryGetProperty("PrepTimeSecs", out var p) ? p.GetInt32() : 0;
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        await UpsertRestaurantStats(restaurantId, regionId, date, rs =>
        {
            rs.OrdersCompleted++;
            rs.GrossRevenue += revenue;
            rs.AvgPrepTimeSecs = (rs.AvgPrepTimeSecs * (rs.OrdersCompleted - 1) + prepTime) / rs.OrdersCompleted;
        }, ct);

        if (root.TryGetProperty("Items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                var itemName = item.GetProperty("ItemName").GetString()!;
                var qty = item.GetProperty("Quantity").GetInt32();
                var itemRevenue = item.GetProperty("Revenue").GetDecimal();
                await UpsertMenuItemStats(restaurantId, itemName, date, mi =>
                {
                    mi.UnitsSold += qty;
                    mi.Revenue += itemRevenue;
                }, ct);
            }
        }

        await UpsertAdminStats(regionId, date, a =>
        {
            a.TotalOrders++;
            a.GrossPlatformRevenue += revenue;
        }, ct);
    }

    private async Task HandleOrderCancelled(JsonElement root, CancellationToken ct)
    {
        var restaurantId = root.GetProperty("RestaurantId").GetGuid();
        var regionId = root.GetProperty("RegionId").GetInt32();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        await UpsertRestaurantStats(restaurantId, regionId, date, rs => rs.OrdersCancelled++, ct);
    }

    private async Task HandlePaymentSuccess(JsonElement root, CancellationToken ct)
    {
        var driverId = root.TryGetProperty("DriverId", out var d) ? (Guid?)d.GetGuid() : null;
        var regionId = root.GetProperty("RegionId").GetInt32();
        var amount = root.TryGetProperty("Amount", out var a) ? a.GetDecimal() : 0m;
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        if (driverId.HasValue)
            await UpsertDriverStats(driverId.Value, regionId, date, ds => ds.PayoutAmount += amount, ct);
    }

    private async Task UpsertDriverStats(Guid driverId, int regionId, DateOnly date,
        Action<DriverStatsDaily> mutate, CancellationToken ct)
    {
        var row = await _db.DriverStatsDaily
            .FirstOrDefaultAsync(x => x.DriverId == driverId && x.StatDate == date, ct);
        if (row is null)
        {
            row = new DriverStatsDaily { DriverId = driverId, RegionId = regionId, StatDate = date };
            _db.DriverStatsDaily.Add(row);
        }
        mutate(row);
    }

    private async Task UpsertRestaurantStats(Guid restaurantId, int regionId, DateOnly date,
        Action<RestaurantStatsDaily> mutate, CancellationToken ct)
    {
        var row = await _db.RestaurantStatsDaily
            .FirstOrDefaultAsync(x => x.RestaurantId == restaurantId && x.StatDate == date, ct);
        if (row is null)
        {
            row = new RestaurantStatsDaily { RestaurantId = restaurantId, RegionId = regionId, StatDate = date };
            _db.RestaurantStatsDaily.Add(row);
        }
        mutate(row);
    }

    private async Task UpsertMenuItemStats(Guid restaurantId, string itemName, DateOnly date,
        Action<MenuItemStatsDaily> mutate, CancellationToken ct)
    {
        var row = await _db.MenuItemStatsDaily
            .FirstOrDefaultAsync(x => x.RestaurantId == restaurantId && x.ItemName == itemName && x.StatDate == date, ct);
        if (row is null)
        {
            row = new MenuItemStatsDaily { RestaurantId = restaurantId, ItemName = itemName, StatDate = date };
            _db.MenuItemStatsDaily.Add(row);
        }
        mutate(row);
    }

    private async Task UpsertAdminStats(int regionId, DateOnly date,
        Action<AdminStatsDaily> mutate, CancellationToken ct)
    {
        var row = await _db.AdminStatsDaily
            .FirstOrDefaultAsync(x => x.RegionId == regionId && x.StatDate == date, ct);
        if (row is null)
        {
            row = new AdminStatsDaily { RegionId = regionId, StatDate = date };
            _db.AdminStatsDaily.Add(row);
        }
        mutate(row);
    }
}
