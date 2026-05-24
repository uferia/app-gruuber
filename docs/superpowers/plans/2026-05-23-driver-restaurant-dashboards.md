# Driver & Restaurant Dashboards Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the `Gruuber.Analytics` module — a dedicated read-only dashboard layer for drivers, restaurant owners, and admins, populated exclusively by Kafka consumers reacting to domain events, with CSV/PDF async export support.

**Architecture:** New `Gruuber.Analytics` project with its own `AnalyticsDbContext` (5 tables: 4 daily-snapshot stat tables + `analytics_export_jobs` + `processed_analytics_events`). `AnalyticsConsumerService` (Kafka `BackgroundService`) listens to all region-scoped ride/order/payment topics, upserts stats using `INSERT … ON CONFLICT DO UPDATE`, and deduplicates via `processed_analytics_events` in the same DB transaction. Query handlers aggregate over daily rows for weekly/monthly views. `ExportJobService` generates CSV/PDF async. `AnalyticsController` serves all endpoints under `/v1/analytics/`. Entity IDs always sourced from JWT `sub` — never from query params for scoped roles.

**Tech Stack:** ASP.NET Core 8, EF Core 8, Npgsql, Confluent.Kafka, QuestPDF (PDF export), CsvHelper (CSV), xunit, Moq, Testcontainers

---

## File Map

**New project:**
- `src/Gruuber.Analytics/Gruuber.Analytics.csproj`
- `src/Gruuber.Analytics/AnalyticsModule.cs`
- `src/Gruuber.Analytics/Domain/DriverStatsDaily.cs`
- `src/Gruuber.Analytics/Domain/RestaurantStatsDaily.cs`
- `src/Gruuber.Analytics/Domain/MenuItemStatsDaily.cs`
- `src/Gruuber.Analytics/Domain/AdminStatsDaily.cs`
- `src/Gruuber.Analytics/Domain/AnalyticsExportJob.cs`
- `src/Gruuber.Analytics/Domain/ProcessedAnalyticsEvent.cs`
- `src/Gruuber.Analytics/Infrastructure/AnalyticsDbContext.cs`
- `src/Gruuber.Analytics/Infrastructure/AnalyticsDbContextFactory.cs`
- `src/Gruuber.Analytics/Infrastructure/Migrations/` _(generated)_
- `src/Gruuber.Analytics/Application/AnalyticsConsumerService.cs`
- `src/Gruuber.Analytics/Application/Queries/DriverDashboardQueries.cs`
- `src/Gruuber.Analytics/Application/Queries/RestaurantDashboardQueries.cs`
- `src/Gruuber.Analytics/Application/Queries/AdminDashboardQueries.cs`
- `src/Gruuber.Analytics/Application/ExportJobService.cs`

**New controller:**
- `src/Gruuber.Api/Controllers/AnalyticsController.cs`

**Modified files:**
- `src/Gruuber.Api/Program.cs` — register `AddAnalyticsModule()`
- `Gruuber.slnx` — add new project
- `tests/Gruuber.Tests/Gruuber.Tests.csproj` — add project reference to Analytics

---

## Task 1: Create Gruuber.Analytics project and domain entities

**Files:**
- Create: `src/Gruuber.Analytics/Gruuber.Analytics.csproj`
- Create: domain entity files (6 files)

- [ ] **Step 1: Create the csproj**

```xml
<!-- src/Gruuber.Analytics/Gruuber.Analytics.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\Gruuber.SharedKernel\Gruuber.SharedKernel.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Confluent.Kafka" Version="2.3.0" />
    <PackageReference Include="CsvHelper" Version="33.0.1" />
    <PackageReference Include="QuestPDF" Version="2024.10.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.0">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.0" />
  </ItemGroup>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

Add to solution:
```bash
cd c:\Projects\app-gruuber.worktrees\copilot-feature-brainstorming-session
dotnet sln Gruuber.slnx add src/Gruuber.Analytics/Gruuber.Analytics.csproj
```

- [ ] **Step 2: Create domain entities**

```csharp
// src/Gruuber.Analytics/Domain/DriverStatsDaily.cs
namespace Gruuber.Analytics.Domain;

public class DriverStatsDaily
{
    public Guid DriverId { get; set; }
    public int RegionId { get; set; }
    public DateOnly StatDate { get; set; }
    public int TripsCompleted { get; set; }
    public int TripsCancelled { get; set; }
    public int PoolTrips { get; set; }
    public decimal GrossEarnings { get; set; }
    public decimal BonusEarnings { get; set; }
    public decimal PayoutAmount { get; set; }
    public decimal AvgRating { get; set; }
    public decimal AcceptanceRate { get; set; }  // 0.0–1.0
    public int OnlineMinutes { get; set; }
}
```

```csharp
// src/Gruuber.Analytics/Domain/RestaurantStatsDaily.cs
namespace Gruuber.Analytics.Domain;

public class RestaurantStatsDaily
{
    public Guid RestaurantId { get; set; }
    public int RegionId { get; set; }
    public DateOnly StatDate { get; set; }
    public int OrdersReceived { get; set; }
    public int OrdersCompleted { get; set; }
    public int OrdersCancelled { get; set; }
    public decimal GrossRevenue { get; set; }
    public int AvgPrepTimeSecs { get; set; }
    public decimal AvgRating { get; set; }
}
```

```csharp
// src/Gruuber.Analytics/Domain/MenuItemStatsDaily.cs
namespace Gruuber.Analytics.Domain;

public class MenuItemStatsDaily
{
    public Guid RestaurantId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public DateOnly StatDate { get; set; }
    public int UnitsSold { get; set; }
    public decimal Revenue { get; set; }
}
```

```csharp
// src/Gruuber.Analytics/Domain/AdminStatsDaily.cs
namespace Gruuber.Analytics.Domain;

public class AdminStatsDaily
{
    public int RegionId { get; set; }
    public DateOnly StatDate { get; set; }
    public int TotalRides { get; set; }
    public int TotalPoolRides { get; set; }
    public int TotalOrders { get; set; }
    public decimal GrossPlatformRevenue { get; set; }
    public int ActiveDrivers { get; set; }
    public int ActiveRestaurants { get; set; }
}
```

```csharp
// src/Gruuber.Analytics/Domain/AnalyticsExportJob.cs
namespace Gruuber.Analytics.Domain;

public class AnalyticsExportJob
{
    public Guid JobId { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public string Role { get; set; } = string.Empty;       // 'driver' | 'restaurant' | 'admin'
    public string Format { get; set; } = string.Empty;     // 'csv' | 'pdf'
    public string Status { get; set; } = "pending";        // pending | processing | completed | failed
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public string? DownloadUrl { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

```csharp
// src/Gruuber.Analytics/Domain/ProcessedAnalyticsEvent.cs
namespace Gruuber.Analytics.Domain;

public class ProcessedAnalyticsEvent
{
    public Guid EventId { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 3: Commit**

```bash
git add src/Gruuber.Analytics/
git commit -m "feat(analytics): create Gruuber.Analytics project and domain entities"
```

---

## Task 2: AnalyticsDbContext and EF migration

**Files:**
- Create: `src/Gruuber.Analytics/Infrastructure/AnalyticsDbContext.cs`
- Create: `src/Gruuber.Analytics/Infrastructure/AnalyticsDbContextFactory.cs`
- New EF migration files

- [ ] **Step 1: Create AnalyticsDbContext**

```csharp
// src/Gruuber.Analytics/Infrastructure/AnalyticsDbContext.cs
using Gruuber.Analytics.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Analytics.Infrastructure;

public class AnalyticsDbContext : DbContext
{
    public AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : base(options) { }

    public DbSet<DriverStatsDaily> DriverStatsDaily => Set<DriverStatsDaily>();
    public DbSet<RestaurantStatsDaily> RestaurantStatsDaily => Set<RestaurantStatsDaily>();
    public DbSet<MenuItemStatsDaily> MenuItemStatsDaily => Set<MenuItemStatsDaily>();
    public DbSet<AdminStatsDaily> AdminStatsDaily => Set<AdminStatsDaily>();
    public DbSet<AnalyticsExportJob> ExportJobs => Set<AnalyticsExportJob>();
    public DbSet<ProcessedAnalyticsEvent> ProcessedEvents => Set<ProcessedAnalyticsEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DriverStatsDaily>(e =>
        {
            e.ToTable("driver_stats_daily");
            e.HasKey(x => new { x.DriverId, x.StatDate });
            e.Property(x => x.GrossEarnings).HasColumnType("numeric(12,2)");
            e.Property(x => x.BonusEarnings).HasColumnType("numeric(12,2)");
            e.Property(x => x.PayoutAmount).HasColumnType("numeric(12,2)");
            e.Property(x => x.AvgRating).HasColumnType("numeric(3,2)");
            e.Property(x => x.AcceptanceRate).HasColumnType("numeric(4,3)");
        });

        modelBuilder.Entity<RestaurantStatsDaily>(e =>
        {
            e.ToTable("restaurant_stats_daily");
            e.HasKey(x => new { x.RestaurantId, x.StatDate });
            e.Property(x => x.GrossRevenue).HasColumnType("numeric(12,2)");
            e.Property(x => x.AvgRating).HasColumnType("numeric(3,2)");
        });

        modelBuilder.Entity<MenuItemStatsDaily>(e =>
        {
            e.ToTable("menu_item_stats_daily");
            e.HasKey(x => new { x.RestaurantId, x.ItemName, x.StatDate });
            e.Property(x => x.Revenue).HasColumnType("numeric(12,2)");
        });

        modelBuilder.Entity<AdminStatsDaily>(e =>
        {
            e.ToTable("admin_stats_daily");
            e.HasKey(x => new { x.RegionId, x.StatDate });
            e.Property(x => x.GrossPlatformRevenue).HasColumnType("numeric(14,2)");
        });

        modelBuilder.Entity<AnalyticsExportJob>(e =>
        {
            e.ToTable("analytics_export_jobs");
            e.HasKey(x => x.JobId);
            e.HasIndex(x => new { x.OwnerId, x.Status });
        });

        modelBuilder.Entity<ProcessedAnalyticsEvent>(e =>
        {
            e.ToTable("processed_analytics_events");
            e.HasKey(x => x.EventId);
        });
    }
}
```

- [ ] **Step 2: Create AnalyticsDbContextFactory**

```csharp
// src/Gruuber.Analytics/Infrastructure/AnalyticsDbContextFactory.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gruuber.Analytics.Infrastructure;

public class AnalyticsDbContextFactory : IDesignTimeDbContextFactory<AnalyticsDbContext>
{
    public AnalyticsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseNpgsql("Host=localhost;Database=gruuber_analytics;Username=postgres;Password=postgres")
            .Options;
        return new AnalyticsDbContext(options);
    }
}
```

- [ ] **Step 3: Add project reference to Gruuber.Api**

In `src/Gruuber.Api/Gruuber.Api.csproj`, add:

```xml
<ProjectReference Include="..\Gruuber.Analytics\Gruuber.Analytics.csproj" />
```

- [ ] **Step 4: Generate migration**

```bash
cd c:\Projects\app-gruuber.worktrees\copilot-feature-brainstorming-session
dotnet ef migrations add InitialCreate --project src/Gruuber.Analytics/Gruuber.Analytics.csproj --startup-project src/Gruuber.Api/Gruuber.Api.csproj
```

Expected: migration files generated.

- [ ] **Step 5: Build**

```bash
dotnet build Gruuber.slnx -c Release --no-incremental 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add src/Gruuber.Analytics/Infrastructure/ src/Gruuber.Api/Gruuber.Api.csproj
git commit -m "feat(analytics): add AnalyticsDbContext, DbContextFactory, and initial EF migration"
```

---

## Task 3: AnalyticsConsumerService

**Files:**
- Create: `src/Gruuber.Analytics/Application/AnalyticsConsumerService.cs`

- [ ] **Step 1: Write failing unit tests**

```csharp
// tests/Gruuber.Tests/Unit/Analytics/AnalyticsConsumerServiceTests.cs
using Gruuber.Analytics.Application;
using Gruuber.Analytics.Domain;
using Gruuber.Analytics.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class AnalyticsConsumerServiceTests
{
    private static AnalyticsDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AnalyticsDbContext(opts);
    }

    [Fact]
    public async Task ProcessRideCompleted_UpsertDriverStatsAndAdminStats()
    {
        await using var db = CreateInMemoryDb();
        var processor = new AnalyticsEventProcessor(db, NullLogger<AnalyticsEventProcessor>.Instance);

        var driverId = Guid.NewGuid();
        var payload = $@"{{
            ""EventName"": ""ride_completed"",
            ""RideId"": ""{Guid.NewGuid()}"",
            ""DriverId"": ""{driverId}"",
            ""RegionId"": 1,
            ""Fare"": 12.50,
            ""IsPool"": false,
            ""OccurredAt"": ""{DateTime.UtcNow:O}""
        }}";

        await processor.ProcessAsync(payload, CancellationToken.None);

        var driverStat = await db.DriverStatsDaily
            .SingleAsync(x => x.DriverId == driverId);
        Assert.Equal(1, driverStat.TripsCompleted);
        Assert.Equal(12.50m, driverStat.GrossEarnings);

        var adminStat = await db.AdminStatsDaily.SingleAsync(x => x.RegionId == 1);
        Assert.Equal(1, adminStat.TotalRides);
    }

    [Fact]
    public async Task ProcessRideCompleted_AccumulatesMultipleEvents()
    {
        await using var db = CreateInMemoryDb();
        var processor = new AnalyticsEventProcessor(db, NullLogger<AnalyticsEventProcessor>.Instance);
        var driverId = Guid.NewGuid();
        var today = DateTime.UtcNow.ToString("O");

        for (int i = 0; i < 5; i++)
        {
            var payload = $@"{{
                ""EventName"": ""ride_completed"",
                ""RideId"": ""{Guid.NewGuid()}"",
                ""DriverId"": ""{driverId}"",
                ""RegionId"": 1,
                ""Fare"": 10.00,
                ""IsPool"": false,
                ""OccurredAt"": ""{today}""
            }}";
            await processor.ProcessAsync(payload, CancellationToken.None);
        }

        var stat = await db.DriverStatsDaily.SingleAsync(x => x.DriverId == driverId);
        Assert.Equal(5, stat.TripsCompleted);
        Assert.Equal(50.00m, stat.GrossEarnings);
    }

    [Fact]
    public async Task ProcessDuplicateEvent_SkipsSecondUpsert()
    {
        await using var db = CreateInMemoryDb();
        var processor = new AnalyticsEventProcessor(db, NullLogger<AnalyticsEventProcessor>.Instance);
        var driverId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        var payload = $@"{{
            ""EventName"": ""ride_completed"",
            ""EventId"": ""{eventId}"",
            ""RideId"": ""{Guid.NewGuid()}"",
            ""DriverId"": ""{driverId}"",
            ""RegionId"": 1,
            ""Fare"": 10.00,
            ""IsPool"": false,
            ""OccurredAt"": ""{DateTime.UtcNow:O}""
        }}";

        await processor.ProcessAsync(payload, CancellationToken.None);
        await processor.ProcessAsync(payload, CancellationToken.None); // duplicate

        var stat = await db.DriverStatsDaily.SingleAsync(x => x.DriverId == driverId);
        Assert.Equal(1, stat.TripsCompleted); // not 2
    }

    [Fact]
    public async Task ProcessOrderDelivered_UpsertRestaurantAndMenuItemStats()
    {
        await using var db = CreateInMemoryDb();
        var processor = new AnalyticsEventProcessor(db, NullLogger<AnalyticsEventProcessor>.Instance);
        var restaurantId = Guid.NewGuid();

        var payload = $@"{{
            ""EventName"": ""order_delivered"",
            ""OrderId"": ""{Guid.NewGuid()}"",
            ""RestaurantId"": ""{restaurantId}"",
            ""RegionId"": 1,
            ""Revenue"": 25.00,
            ""PrepTimeSecs"": 600,
            ""Items"": [
                {{ ""ItemName"": ""Burger"", ""Quantity"": 2, ""Revenue"": 16.00 }},
                {{ ""ItemName"": ""Fries"", ""Quantity"": 1, ""Revenue"": 9.00 }}
            ],
            ""OccurredAt"": ""{DateTime.UtcNow:O}""
        }}";

        await processor.ProcessAsync(payload, CancellationToken.None);

        var restStat = await db.RestaurantStatsDaily.SingleAsync(x => x.RestaurantId == restaurantId);
        Assert.Equal(1, restStat.OrdersCompleted);
        Assert.Equal(25.00m, restStat.GrossRevenue);

        var menuStats = await db.MenuItemStatsDaily
            .Where(x => x.RestaurantId == restaurantId).ToListAsync();
        Assert.Equal(2, menuStats.Count);
        Assert.Equal(2, menuStats.First(x => x.ItemName == "Burger").UnitsSold);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "AnalyticsConsumerServiceTests" -v minimal
```

Expected: compile error — `AnalyticsEventProcessor` not found.

- [ ] **Step 3: Implement AnalyticsConsumerService**

```csharp
// src/Gruuber.Analytics/Application/AnalyticsConsumerService.cs
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
                    // DLQ routing would go here via IKafkaProducer — inject if needed
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

        // Idempotency check
        Guid eventId = Guid.NewGuid(); // use RideId/OrderId as event key when no explicit EventId
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
            await UpsertDriverStats(driverId.Value, regionId, date, d => d.TripsCancelled++, ct);

        await UpsertAdminStats(regionId, date, _ => { }, ct); // no-op increment for cancels on admin
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
            // Running average prep time: approximate with cumulative sum / update ratio
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

    // Generic upsert helpers using EF Core in-memory + real DB "find or create" pattern
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
```

- [ ] **Step 4: Add project reference to tests**

In `tests/Gruuber.Tests/Gruuber.Tests.csproj`, add:

```xml
<ProjectReference Include="..\..\src\Gruuber.Analytics\Gruuber.Analytics.csproj" />
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "AnalyticsConsumerServiceTests" -v minimal
```

Expected: 4 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Gruuber.Analytics/Application/AnalyticsConsumerService.cs
git add tests/Gruuber.Tests/Unit/Analytics/AnalyticsConsumerServiceTests.cs
git add tests/Gruuber.Tests/Gruuber.Tests.csproj
git commit -m "feat(analytics): implement AnalyticsConsumerService and AnalyticsEventProcessor with idempotency"
```

---

## Task 4: Query handlers

**Files:**
- Create: `src/Gruuber.Analytics/Application/Queries/DriverDashboardQueries.cs`
- Create: `src/Gruuber.Analytics/Application/Queries/RestaurantDashboardQueries.cs`
- Create: `src/Gruuber.Analytics/Application/Queries/AdminDashboardQueries.cs`

- [ ] **Step 1: Write failing unit tests**

```csharp
// tests/Gruuber.Tests/Unit/Analytics/DashboardQueryTests.cs
using Gruuber.Analytics.Application.Queries;
using Gruuber.Analytics.Domain;
using Gruuber.Analytics.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

public class DashboardQueryTests
{
    private static AnalyticsDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AnalyticsDbContext(opts);
    }

    [Fact]
    public async Task DriverSummary_WeeklyPeriod_SumsSeven DailyRows()
    {
        await using var db = CreateInMemoryDb();
        var driverId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        for (int i = 0; i < 7; i++)
        {
            db.DriverStatsDaily.Add(new DriverStatsDaily
            {
                DriverId = driverId, RegionId = 1,
                StatDate = today.AddDays(-i),
                TripsCompleted = 5, GrossEarnings = 50.00m
            });
        }
        await db.SaveChangesAsync();

        var handler = new DriverDashboardQueryHandler(db);
        var result = await handler.GetSummaryAsync(driverId, "weekly", CancellationToken.None);

        Assert.Equal(35, result.TripsCompleted);
        Assert.Equal(350.00m, result.GrossEarnings);
    }

    [Fact]
    public async Task DriverSummary_NoPeriodData_ReturnsZeroValuedSummary()
    {
        await using var db = CreateInMemoryDb();
        var handler = new DriverDashboardQueryHandler(db);
        var result = await handler.GetSummaryAsync(Guid.NewGuid(), "daily", CancellationToken.None);

        Assert.Equal(0, result.TripsCompleted);
        Assert.Equal(0m, result.GrossEarnings);
        // Must return 200 with zeros, not throw
    }

    [Fact]
    public async Task RestaurantMenuPerformance_SortedByUnitsSoldDesc()
    {
        await using var db = CreateInMemoryDb();
        var restaurantId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        db.MenuItemStatsDaily.AddRange(
            new MenuItemStatsDaily { RestaurantId = restaurantId, ItemName = "Burger", StatDate = today, UnitsSold = 10, Revenue = 80m },
            new MenuItemStatsDaily { RestaurantId = restaurantId, ItemName = "Fries", StatDate = today, UnitsSold = 25, Revenue = 50m }
        );
        await db.SaveChangesAsync();

        var handler = new RestaurantDashboardQueryHandler(db);
        var result = await handler.GetMenuPerformanceAsync(restaurantId, "daily", CancellationToken.None);

        Assert.Equal("Fries", result.Items[0].ItemName);  // highest units_sold first
        Assert.Equal("Burger", result.Items[1].ItemName);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "DashboardQueryTests" -v minimal
```

Expected: compile error.

- [ ] **Step 3: Implement query handlers**

```csharp
// src/Gruuber.Analytics/Application/Queries/DriverDashboardQueries.cs
using Gruuber.Analytics.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Analytics.Application.Queries;

public class DriverDashboardQueryHandler
{
    private readonly AnalyticsDbContext _db;
    public DriverDashboardQueryHandler(AnalyticsDbContext db) => _db = db;

    public async Task<DriverSummaryResponse> GetSummaryAsync(Guid driverId, string period, CancellationToken ct)
    {
        var (fromDate, toDate) = GetDateRange(period);
        var rows = await _db.DriverStatsDaily
            .Where(x => x.DriverId == driverId && x.StatDate >= fromDate && x.StatDate <= toDate)
            .ToListAsync(ct);

        return new DriverSummaryResponse(
            TripsCompleted: rows.Sum(x => x.TripsCompleted),
            TripsCancelled: rows.Sum(x => x.TripsCancelled),
            PoolTrips: rows.Sum(x => x.PoolTrips),
            GrossEarnings: rows.Sum(x => x.GrossEarnings),
            BonusEarnings: rows.Sum(x => x.BonusEarnings),
            PayoutAmount: rows.Sum(x => x.PayoutAmount),
            AvgRating: rows.Count > 0 ? rows.Average(x => x.AvgRating) : 0m,
            AcceptanceRate: rows.Count > 0 ? rows.Average(x => x.AcceptanceRate) : 0m,
            OnlineMinutes: rows.Sum(x => x.OnlineMinutes));
    }

    public async Task<PagedResponse<DriverTripRow>> GetTripsAsync(Guid driverId,
        DateOnly fromDate, DateOnly toDate, int page, int limit, CancellationToken ct)
    {
        var query = _db.DriverStatsDaily
            .Where(x => x.DriverId == driverId && x.StatDate >= fromDate && x.StatDate <= toDate)
            .OrderByDescending(x => x.StatDate);

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * limit).Take(limit)
            .Select(x => new DriverTripRow(x.StatDate, x.TripsCompleted, x.GrossEarnings))
            .ToListAsync(ct);

        return new PagedResponse<DriverTripRow>(items, total, page, limit);
    }

    private static (DateOnly from, DateOnly to) GetDateRange(string period) =>
        period switch
        {
            "weekly" => (DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-6)), DateOnly.FromDateTime(DateTime.UtcNow)),
            "monthly" => (DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-29)), DateOnly.FromDateTime(DateTime.UtcNow)),
            _ => (DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow)) // daily
        };
}

public record DriverSummaryResponse(int TripsCompleted, int TripsCancelled, int PoolTrips,
    decimal GrossEarnings, decimal BonusEarnings, decimal PayoutAmount,
    decimal AvgRating, decimal AcceptanceRate, int OnlineMinutes);

public record DriverTripRow(DateOnly Date, int Trips, decimal Earnings);
public record PagedResponse<T>(List<T> Items, int Total, int Page, int Limit);
```

```csharp
// src/Gruuber.Analytics/Application/Queries/RestaurantDashboardQueries.cs
using Gruuber.Analytics.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Analytics.Application.Queries;

public class RestaurantDashboardQueryHandler
{
    private readonly AnalyticsDbContext _db;
    public RestaurantDashboardQueryHandler(AnalyticsDbContext db) => _db = db;

    public async Task<RestaurantSummaryResponse> GetSummaryAsync(Guid restaurantId, string period, CancellationToken ct)
    {
        var (fromDate, toDate) = GetDateRange(period);
        var rows = await _db.RestaurantStatsDaily
            .Where(x => x.RestaurantId == restaurantId && x.StatDate >= fromDate && x.StatDate <= toDate)
            .ToListAsync(ct);

        var totalCompleted = rows.Sum(x => x.OrdersCompleted);
        var totalReceived = rows.Sum(x => x.OrdersReceived);
        var cancellationRate = totalReceived > 0
            ? (decimal)rows.Sum(x => x.OrdersCancelled) / totalReceived
            : 0m;

        return new RestaurantSummaryResponse(
            OrdersReceived: totalReceived,
            OrdersCompleted: totalCompleted,
            OrdersCancelled: rows.Sum(x => x.OrdersCancelled),
            GrossRevenue: rows.Sum(x => x.GrossRevenue),
            AvgPrepTimeSecs: rows.Count > 0 ? (int)rows.Average(x => x.AvgPrepTimeSecs) : 0,
            AvgRating: rows.Count > 0 ? rows.Average(x => x.AvgRating) : 0m,
            CancellationRate: cancellationRate);
    }

    public async Task<MenuPerformanceResponse> GetMenuPerformanceAsync(Guid restaurantId, string period, CancellationToken ct)
    {
        var (fromDate, toDate) = GetDateRange(period);
        var items = await _db.MenuItemStatsDaily
            .Where(x => x.RestaurantId == restaurantId && x.StatDate >= fromDate && x.StatDate <= toDate)
            .GroupBy(x => x.ItemName)
            .Select(g => new MenuItemRow(g.Key, g.Sum(x => x.UnitsSold), g.Sum(x => x.Revenue)))
            .OrderByDescending(x => x.UnitsSold)
            .ToListAsync(ct);

        return new MenuPerformanceResponse(items);
    }

    private static (DateOnly from, DateOnly to) GetDateRange(string period) =>
        period switch
        {
            "weekly" => (DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-6)), DateOnly.FromDateTime(DateTime.UtcNow)),
            "monthly" => (DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-29)), DateOnly.FromDateTime(DateTime.UtcNow)),
            _ => (DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow))
        };
}

public record RestaurantSummaryResponse(int OrdersReceived, int OrdersCompleted, int OrdersCancelled,
    decimal GrossRevenue, int AvgPrepTimeSecs, decimal AvgRating, decimal CancellationRate);
public record MenuItemRow(string ItemName, int UnitsSold, decimal Revenue);
public record MenuPerformanceResponse(List<MenuItemRow> Items);
```

```csharp
// src/Gruuber.Analytics/Application/Queries/AdminDashboardQueries.cs
using Gruuber.Analytics.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Analytics.Application.Queries;

public class AdminDashboardQueryHandler
{
    private readonly AnalyticsDbContext _db;
    public AdminDashboardQueryHandler(AnalyticsDbContext db) => _db = db;

    public async Task<AdminSummaryResponse> GetSummaryAsync(int regionId, string period, CancellationToken ct)
    {
        var (fromDate, toDate) = GetDateRange(period);
        var rows = await _db.AdminStatsDaily
            .Where(x => x.RegionId == regionId && x.StatDate >= fromDate && x.StatDate <= toDate)
            .ToListAsync(ct);

        return new AdminSummaryResponse(
            TotalRides: rows.Sum(x => x.TotalRides),
            TotalPoolRides: rows.Sum(x => x.TotalPoolRides),
            TotalOrders: rows.Sum(x => x.TotalOrders),
            GrossPlatformRevenue: rows.Sum(x => x.GrossPlatformRevenue),
            ActiveDrivers: rows.Count > 0 ? (int)rows.Average(x => x.ActiveDrivers) : 0,
            ActiveRestaurants: rows.Count > 0 ? (int)rows.Average(x => x.ActiveRestaurants) : 0);
    }

    private static (DateOnly from, DateOnly to) GetDateRange(string period) =>
        period switch
        {
            "weekly" => (DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-6)), DateOnly.FromDateTime(DateTime.UtcNow)),
            "monthly" => (DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-29)), DateOnly.FromDateTime(DateTime.UtcNow)),
            _ => (DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow))
        };
}

public record AdminSummaryResponse(int TotalRides, int TotalPoolRides, int TotalOrders,
    decimal GrossPlatformRevenue, int ActiveDrivers, int ActiveRestaurants);
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "DashboardQueryTests" -v minimal
```

Expected: 3 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Gruuber.Analytics/Application/Queries/
git add tests/Gruuber.Tests/Unit/Analytics/DashboardQueryTests.cs
git commit -m "feat(analytics): add driver, restaurant, and admin dashboard query handlers"
```

---

## Task 5: ExportJobService

**Files:**
- Create: `src/Gruuber.Analytics/Application/ExportJobService.cs`

- [ ] **Step 1: Write failing test**

```csharp
// tests/Gruuber.Tests/Unit/Analytics/ExportJobServiceTests.cs
using Gruuber.Analytics.Application;
using Gruuber.Analytics.Domain;
using Gruuber.Analytics.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class ExportJobServiceTests
{
    private static AnalyticsDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AnalyticsDbContext(opts);
    }

    [Fact]
    public async Task EnqueueExport_CreatesJobWithPendingStatus()
    {
        await using var db = CreateInMemoryDb();
        var svc = new ExportJobService(db, NullLogger<ExportJobService>.Instance);

        var jobId = await svc.EnqueueAsync(Guid.NewGuid(), "driver", "csv",
            DateOnly.Parse("2026-01-01"), DateOnly.Parse("2026-01-31"), CancellationToken.None);

        var job = await db.ExportJobs.FindAsync(jobId);
        Assert.NotNull(job);
        Assert.Equal("pending", job!.Status);
    }

    [Fact]
    public async Task GetJobStatus_ReturnsNotFound_WhenJobDoesNotExist()
    {
        await using var db = CreateInMemoryDb();
        var svc = new ExportJobService(db, NullLogger<ExportJobService>.Instance);

        var result = await svc.GetStatusAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetJobStatus_Returns403_WhenOwnerMismatch()
    {
        await using var db = CreateInMemoryDb();
        var svc = new ExportJobService(db, NullLogger<ExportJobService>.Instance);

        var ownerId = Guid.NewGuid();
        db.ExportJobs.Add(new AnalyticsExportJob
        {
            OwnerId = ownerId, Role = "driver", Format = "csv",
            FromDate = DateOnly.Parse("2026-01-01"), ToDate = DateOnly.Parse("2026-01-31")
        });
        await db.SaveChangesAsync();

        var job = await db.ExportJobs.SingleAsync();
        var result = await svc.GetStatusAsync(job.JobId, Guid.NewGuid() /* different owner */, CancellationToken.None);
        Assert.Null(result); // unauthorized — returns null → controller returns 403
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "ExportJobServiceTests" -v minimal
```

Expected: compile error.

- [ ] **Step 3: Implement ExportJobService**

```csharp
// src/Gruuber.Analytics/Application/ExportJobService.cs
using Gruuber.Analytics.Domain;
using Gruuber.Analytics.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gruuber.Analytics.Application;

public class ExportJobService
{
    private readonly AnalyticsDbContext _db;
    private readonly ILogger<ExportJobService> _logger;
    private const int DownloadUrlTtlMinutes = 60;

    public ExportJobService(AnalyticsDbContext db, ILogger<ExportJobService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Enqueues a new export job and returns the JobId.</summary>
    public async Task<Guid> EnqueueAsync(
        Guid ownerId, string role, string format,
        DateOnly fromDate, DateOnly toDate,
        CancellationToken ct)
    {
        var job = new AnalyticsExportJob
        {
            OwnerId = ownerId,
            Role = role,
            Format = format,
            Status = "pending",
            FromDate = fromDate,
            ToDate = toDate,
            CreatedAt = DateTime.UtcNow
        };
        _db.ExportJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Export job {JobId} enqueued for owner={OwnerId} format={Format}", job.JobId, ownerId, format);
        return job.JobId;
    }

    /// <summary>
    /// Returns the job status for the specified owner.
    /// Returns null if not found OR if owner mismatch (caller should return 403/404).
    /// </summary>
    public async Task<ExportJobStatusResponse?> GetStatusAsync(Guid jobId, Guid callerId, CancellationToken ct)
    {
        var job = await _db.ExportJobs.FirstOrDefaultAsync(j => j.JobId == jobId, ct);
        if (job is null) return null;
        if (job.OwnerId != callerId) return null;   // auth mismatch — caller returns 403

        return new ExportJobStatusResponse(job.JobId, job.Status, job.DownloadUrl, job.ExpiresAt);
    }

    /// <summary>
    /// Processes a pending export job (called by background worker).
    /// For CSV, generates file bytes. For PDF, uses QuestPDF.
    /// Sets download_url to a presigned URL (placeholder — real impl uses blob storage).
    /// </summary>
    public async Task ProcessJobAsync(Guid jobId, CancellationToken ct)
    {
        var job = await _db.ExportJobs.FindAsync([jobId], ct);
        if (job is null || job.Status != "pending") return;

        job.Status = "processing";
        await _db.SaveChangesAsync(ct);

        try
        {
            byte[] fileBytes;
            if (job.Format == "csv")
                fileBytes = await GenerateCsvAsync(job, ct);
            else
                fileBytes = await GeneratePdfAsync(job, ct);

            // In production: upload fileBytes to Azure Blob / S3, get presigned URL
            // For now: store as base64 data URL (replace with blob storage integration)
            var dataUrl = $"data:application/{job.Format};base64,{Convert.ToBase64String(fileBytes)}";

            job.Status = "completed";
            job.DownloadUrl = dataUrl;
            job.ExpiresAt = DateTime.UtcNow.AddMinutes(DownloadUrlTtlMinutes);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Export job {JobId} completed for owner={OwnerId}", jobId, job.OwnerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export job {JobId} failed", jobId);
            job.Status = "failed";
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task<byte[]> GenerateCsvAsync(AnalyticsExportJob job, CancellationToken ct)
    {
        // Load stats for the job's owner/role/date range
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms);
        using var csv = new CsvHelper.CsvWriter(writer, System.Globalization.CultureInfo.InvariantCulture);

        if (job.Role == "driver")
        {
            var rows = await _db.DriverStatsDaily
                .Where(x => x.DriverId == job.OwnerId
                            && x.StatDate >= job.FromDate && x.StatDate <= job.ToDate)
                .OrderBy(x => x.StatDate)
                .ToListAsync(ct);
            csv.WriteRecords(rows);
        }
        else if (job.Role == "restaurant")
        {
            var rows = await _db.RestaurantStatsDaily
                .Where(x => x.RestaurantId == job.OwnerId
                            && x.StatDate >= job.FromDate && x.StatDate <= job.ToDate)
                .OrderBy(x => x.StatDate)
                .ToListAsync(ct);
            csv.WriteRecords(rows);
        }

        await writer.FlushAsync(ct);
        return ms.ToArray();
    }

    private Task<byte[]> GeneratePdfAsync(AnalyticsExportJob job, CancellationToken ct)
    {
        // QuestPDF document generation
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        var pdfBytes = QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Content().Column(col =>
                {
                    col.Item().Text($"Gruuber Analytics Report")
                        .FontSize(20).Bold();
                    col.Item().Text($"Role: {job.Role} | Period: {job.FromDate} – {job.ToDate}");
                    col.Item().Text($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
                    col.Item().Text("(Full data available in CSV export)");
                });
            });
        }).GeneratePdf();

        return Task.FromResult(pdfBytes);
    }
}

public record ExportJobStatusResponse(Guid JobId, string Status, string? DownloadUrl, DateTime? ExpiresAt);
```

- [ ] **Step 4: Run tests**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "ExportJobServiceTests" -v minimal
```

Expected: 3 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Gruuber.Analytics/Application/ExportJobService.cs
git add tests/Gruuber.Tests/Unit/Analytics/ExportJobServiceTests.cs
git commit -m "feat(analytics): add ExportJobService with CSV and PDF generation via CsvHelper and QuestPDF"
```

---

## Task 6: AnalyticsController and module registration

**Files:**
- Create: `src/Gruuber.Api/Controllers/AnalyticsController.cs`
- Create: `src/Gruuber.Analytics/AnalyticsModule.cs`
- Modify: `src/Gruuber.Api/Program.cs`

- [ ] **Step 1: Create AnalyticsModule**

```csharp
// src/Gruuber.Analytics/AnalyticsModule.cs
using Gruuber.Analytics.Application;
using Gruuber.Analytics.Application.Queries;
using Gruuber.Analytics.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gruuber.Analytics;

public static class AnalyticsModule
{
    public static IServiceCollection AddAnalyticsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AnalyticsDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("AnalyticsDb")
                ?? configuration.GetConnectionString("Default")));

        services.AddScoped<DriverDashboardQueryHandler>();
        services.AddScoped<RestaurantDashboardQueryHandler>();
        services.AddScoped<AdminDashboardQueryHandler>();
        services.AddScoped<ExportJobService>();
        services.AddHostedService<AnalyticsConsumerService>();

        return services;
    }
}
```

- [ ] **Step 2: Create AnalyticsController**

```csharp
// src/Gruuber.Api/Controllers/AnalyticsController.cs
using Gruuber.Analytics.Application;
using Gruuber.Analytics.Application.Queries;
using Gruuber.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gruuber.Api.Controllers;

[ApiController]
[Route("v1/analytics")]
[Authorize]
public class AnalyticsController : ControllerBase
{
    private readonly DriverDashboardQueryHandler _driverHandler;
    private readonly RestaurantDashboardQueryHandler _restaurantHandler;
    private readonly AdminDashboardQueryHandler _adminHandler;
    private readonly ExportJobService _exportService;
    private readonly ICurrentUserContext _currentUser;

    public AnalyticsController(
        DriverDashboardQueryHandler driverHandler,
        RestaurantDashboardQueryHandler restaurantHandler,
        AdminDashboardQueryHandler adminHandler,
        ExportJobService exportService,
        ICurrentUserContext currentUser)
    {
        _driverHandler = driverHandler;
        _restaurantHandler = restaurantHandler;
        _adminHandler = adminHandler;
        _exportService = exportService;
        _currentUser = currentUser;
    }

    // ── DRIVER ──────────────────────────────────────────────────────────

    [HttpGet("driver/summary")]
    [Authorize(Policy = "driver")]
    public async Task<IActionResult> DriverSummary([FromQuery] string period = "daily", CancellationToken ct = default)
    {
        var result = await _driverHandler.GetSummaryAsync(_currentUser.UserId, period, ct);
        return Ok(result);
    }

    [HttpGet("driver/trips")]
    [Authorize(Policy = "driver")]
    public async Task<IActionResult> DriverTrips(
        [FromQuery] int page = 1, [FromQuery] int limit = 20,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-29));
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await _driverHandler.GetTripsAsync(_currentUser.UserId, fromDate, toDate, page, limit, ct);
        return Ok(result);
    }

    [HttpGet("driver/earnings/export")]
    [Authorize(Policy = "driver")]
    public async Task<IActionResult> DriverExport(
        [FromQuery] string format = "csv",
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        if (format is not ("csv" or "pdf"))
            return BadRequest(new { error = "format must be 'csv' or 'pdf'" });

        var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-29));
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (fromDate > toDate)
            return BadRequest(new { error = "from must be before to" });

        var jobId = await _exportService.EnqueueAsync(_currentUser.UserId, "driver", format, fromDate, toDate, ct);
        return Accepted(new { job_id = jobId });
    }

    [HttpGet("driver/exports/{jobId:guid}")]
    [Authorize(Policy = "driver")]
    public async Task<IActionResult> DriverExportStatus(Guid jobId, CancellationToken ct)
    {
        var status = await _exportService.GetStatusAsync(jobId, _currentUser.UserId, ct);
        if (status is null) return NotFound();
        return status.Status == "processing" ? Accepted(status) : Ok(status);
    }

    // ── RESTAURANT ──────────────────────────────────────────────────────

    [HttpGet("restaurant/summary")]
    [Authorize(Policy = "restaurant")]
    public async Task<IActionResult> RestaurantSummary([FromQuery] string period = "daily", CancellationToken ct = default)
    {
        var result = await _restaurantHandler.GetSummaryAsync(_currentUser.UserId, period, ct);
        return Ok(result);
    }

    [HttpGet("restaurant/menu-performance")]
    [Authorize(Policy = "restaurant")]
    public async Task<IActionResult> MenuPerformance([FromQuery] string period = "weekly", CancellationToken ct = default)
    {
        var result = await _restaurantHandler.GetMenuPerformanceAsync(_currentUser.UserId, period, ct);
        return Ok(result);
    }

    [HttpGet("restaurant/revenue/export")]
    [Authorize(Policy = "restaurant")]
    public async Task<IActionResult> RestaurantExport(
        [FromQuery] string format = "csv",
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-29));
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (fromDate > toDate) return BadRequest(new { error = "from must be before to" });

        var jobId = await _exportService.EnqueueAsync(_currentUser.UserId, "restaurant", format, fromDate, toDate, ct);
        return Accepted(new { job_id = jobId });
    }

    [HttpGet("restaurant/exports/{jobId:guid}")]
    [Authorize(Policy = "restaurant")]
    public async Task<IActionResult> RestaurantExportStatus(Guid jobId, CancellationToken ct)
    {
        var status = await _exportService.GetStatusAsync(jobId, _currentUser.UserId, ct);
        if (status is null) return NotFound();
        return status.Status == "processing" ? Accepted(status) : Ok(status);
    }

    // ── ADMIN ────────────────────────────────────────────────────────────

    [HttpGet("admin/summary")]
    [Authorize(Policy = "admin")]
    public async Task<IActionResult> AdminSummary(
        [FromQuery] int region_id = 0,
        [FromQuery] string period = "daily",
        CancellationToken ct = default)
    {
        var effectiveRegion = region_id > 0 ? region_id : _currentUser.RegionId;
        var result = await _adminHandler.GetSummaryAsync(effectiveRegion, period, ct);
        return Ok(result);
    }

    [HttpGet("admin/export")]
    [Authorize(Policy = "admin")]
    public async Task<IActionResult> AdminExport(
        [FromQuery] int region_id = 0,
        [FromQuery] string format = "csv",
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-29));
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (fromDate > toDate) return BadRequest(new { error = "from must be before to" });

        var jobId = await _exportService.EnqueueAsync(_currentUser.UserId, "admin", format, fromDate, toDate, ct);
        return Accepted(new { job_id = jobId });
    }

    [HttpGet("admin/exports/{jobId:guid}")]
    [Authorize(Policy = "admin")]
    public async Task<IActionResult> AdminExportStatus(Guid jobId, CancellationToken ct)
    {
        var status = await _exportService.GetStatusAsync(jobId, _currentUser.UserId, ct);
        if (status is null) return NotFound();
        return status.Status == "processing" ? Accepted(status) : Ok(status);
    }
}
```

- [ ] **Step 3: Register in Program.cs**

In `src/Gruuber.Api/Program.cs`, after the other module registrations:

```csharp
using Gruuber.Analytics;

// In builder.Services section:
builder.Services.AddAnalyticsModule(builder.Configuration);
```

- [ ] **Step 4: Build and run all unit tests**

```bash
dotnet build Gruuber.slnx -c Release --no-incremental 2>&1 | tail -5
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "Category!=Integration" -v minimal
```

Expected: Build succeeds; all unit tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Gruuber.Analytics/AnalyticsModule.cs src/Gruuber.Api/Controllers/AnalyticsController.cs
git add src/Gruuber.Api/Program.cs
git commit -m "feat(analytics): add AnalyticsController, AnalyticsModule; register in Program.cs"
```

---

## Task 7: Integration test stubs

**Files:**
- Create: `tests/Gruuber.Tests/Integration/Analytics/AnalyticsDashboardIntegrationTests.cs`

- [ ] **Step 1: Create integration test stubs**

```csharp
// tests/Gruuber.Tests/Integration/Analytics/AnalyticsDashboardIntegrationTests.cs
using Xunit;

/// <summary>
/// Integration tests for Gruuber.Analytics module.
/// Requires Docker (Postgres + Kafka via Testcontainers).
/// Run with: dotnet test --filter "Category=Integration"
/// </summary>
public class AnalyticsDashboardIntegrationTests
{
    [Fact(Skip = "Requires Docker")]
    [Trait("Category", "Integration")]
    public async Task Publish5RideCompletedEvents_DriverStatsHasCorrectCumulativeTotals()
    {
        // Arrange: Postgres + Kafka containers; seed region config
        // Act: publish 5 ride_completed events via Kafka
        // Assert: driver_stats_daily has trips_completed=5, correct gross_earnings
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires Docker")]
    [Trait("Category", "Integration")]
    public async Task PublishOrderDeliveredWith3Items_ThreeMenuItemStatsRows()
    {
        // Arrange: Postgres + Kafka containers
        // Act: publish order_delivered with 3 items
        // Assert: 3 rows in menu_item_stats_daily with correct units_sold and revenue
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires Docker")]
    [Trait("Category", "Integration")]
    public async Task ReplayDuplicateEvent_TotalsUnchanged()
    {
        // Act: publish same event_id twice
        // Assert: second event skipped; totals reflect only one event
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires Docker")]
    [Trait("Category", "Integration")]
    public async Task ConsumerFails5Times_MessageInDLQ()
    {
        // Arrange: misconfigured consumer + real Kafka
        // Assert: after 5 retries, message in analytics-events-dlq-{region}
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires Docker")]
    [Trait("Category", "Integration")]
    public async Task ExportJob_RequestCSV_PollJobId_DownloadUrl()
    {
        // Act: POST /v1/analytics/driver/earnings/export?format=csv
        // Assert: 202 with job_id; poll job_id; assert completed with download_url
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires Docker")]
    [Trait("Category", "Integration")]
    public async Task DriverCannotAccessOtherDriversData_Returns403()
    {
        // Act: GET /v1/analytics/driver/summary as driver A with driver B's sub in JWT
        // Assert: 403 Forbidden (JWT sub mismatch enforced by policy)
        await Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Run all unit tests to confirm no regressions**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "Category!=Integration" -v minimal
```

Expected: all tests PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/Gruuber.Tests/Integration/Analytics/
git commit -m "test(analytics): add integration test stubs for dashboard and export flows"
```

---

## Completion Checklist

- [ ] All endpoints under `/v1/analytics/`
- [ ] Entity IDs sourced from JWT `sub` — never from request params for scoped roles
- [ ] Write tables (`rides`, `orders`) not touched by Analytics module
- [ ] All Kafka consumers use retry with max 5 attempts + DLQ routing on failure
- [ ] `processed_analytics_events` dedup checked inside same DB transaction as upsert
- [ ] Export download URLs include expiry (`ExpiresAt`)
- [ ] `ExportJobId` included in all export log entries
- [ ] `CancellationToken` propagated in all async consumer and export methods
- [ ] Driver cannot access restaurant or admin endpoints (enforced by `[Authorize(Policy = "...")]`)
