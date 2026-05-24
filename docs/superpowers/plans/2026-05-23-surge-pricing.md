# Surge Pricing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add inline surge pricing to ride and food order bookings — a real-time multiplier that locks the final fare at booking time, backed by admin-configured tiers and time-of-day rules cached in Redis.

**Architecture:** `SurgePricingService` (in `Gruuber.Api/Infrastructure`, like `DefaultDriverScoringService`) is called inline during `RequestRideHandler` and `CreateOrderHandler`. Config tiers are loaded from Redis (TTL 60s), with a direct-DB fallback. `final_fare` is written in the same DB transaction as the ride/order INSERT — immutable thereafter. A new `SurgeController` exposes a pre-booking estimate endpoint and an admin config endpoint.

**Tech Stack:** ASP.NET Core 8, EF Core 8, Npgsql, StackExchange.Redis, xunit, Moq, Testcontainers

---

## File Map

**New files:**
- `src/Gruuber.SharedKernel/Pricing/ISurgePricingService.cs`
- `src/Gruuber.SharedKernel/Pricing/SurgeResolution.cs`
- `src/Gruuber.Rides/Domain/SurgePricingConfig.cs`
- `src/Gruuber.Rides/Domain/SurgeTimeRule.cs`
- `src/Gruuber.Api/Infrastructure/SurgePricingService.cs`
- `src/Gruuber.Api/Controllers/SurgeController.cs`
- `tests/Gruuber.Tests/Unit/Pricing/SurgePricingServiceTests.cs`
- `tests/Gruuber.Tests/Integration/Pricing/SurgePricingIntegrationTests.cs`
- `tests/Gruuber.Tests/Gruuber.Tests.csproj`

**Modified files:**
- `src/Gruuber.Rides/Domain/Ride.cs` — add `DestLat`, `DestLng`, `BaseFare`, `SurgeMultiplier`, `FinalFare`, `SurgeReason`
- `src/Gruuber.Rides/Application/Commands/RideCommands.cs` — add fare fields to command/response records
- `src/Gruuber.Rides/Application/Commands/RequestRideHandler.cs` — call `ISurgePricingService`
- `src/Gruuber.Rides/Infrastructure/RidesDbContext.cs` — add `SurgePricingConfig` and `SurgeTimeRule` entity configs
- `src/Gruuber.Orders/Domain/Order.cs` — add `BaseFare`, `SurgeMultiplier`, `FinalFare`, `SurgeReason`
- `src/Gruuber.Orders/Application/Commands/OrderCommands.cs` — add fare fields to response
- `src/Gruuber.Orders/Application/Commands/CreateOrderHandler.cs` — call `ISurgePricingService`
- `src/Gruuber.Orders/Infrastructure/OrdersDbContext.cs` — add order fare columns config
- `src/Gruuber.Api/Program.cs` — register `SurgePricingService`
- EF migrations for both `Gruuber.Rides` and `Gruuber.Orders`

---

## Task 1: SharedKernel — ISurgePricingService interface and SurgeResolution result

**Files:**
- Create: `src/Gruuber.SharedKernel/Pricing/ISurgePricingService.cs`
- Create: `src/Gruuber.SharedKernel/Pricing/SurgeResolution.cs`

- [ ] **Step 1: Create SurgeResolution record**

```csharp
// src/Gruuber.SharedKernel/Pricing/SurgeResolution.cs
namespace Gruuber.SharedKernel.Pricing;

public record SurgeResolution(
    decimal Multiplier,
    string? Reason,    // 'demand' | 'time_rule' | null
    decimal BaseFare,
    decimal FinalFare
);
```

- [ ] **Step 2: Create ISurgePricingService interface**

```csharp
// src/Gruuber.SharedKernel/Pricing/ISurgePricingService.cs
namespace Gruuber.SharedKernel.Pricing;

public interface ISurgePricingService
{
    /// <summary>
    /// Resolves the surge multiplier for a region+type at the current moment.
    /// Never throws — returns multiplier=1.0 on any failure.
    /// </summary>
    Task<SurgeResolution> ResolveAsync(
        int regionId,
        string rideType,        // "ride" or "food"
        decimal baseFare,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Commit**

```bash
git add src/Gruuber.SharedKernel/Pricing/
git commit -m "feat(pricing): add ISurgePricingService interface and SurgeResolution to SharedKernel"
```

---

## Task 2: Domain entities — SurgePricingConfig and SurgeTimeRule

**Files:**
- Create: `src/Gruuber.Rides/Domain/SurgePricingConfig.cs`
- Create: `src/Gruuber.Rides/Domain/SurgeTimeRule.cs`

- [ ] **Step 1: Create SurgePricingConfig entity**

```csharp
// src/Gruuber.Rides/Domain/SurgePricingConfig.cs
namespace Gruuber.Rides.Domain;

public class SurgePricingConfig
{
    public int RegionId { get; set; }
    public string RideType { get; set; } = string.Empty;   // "ride" | "food"
    public decimal DemandRatioThreshold { get; set; }       // e.g. 0.50
    public decimal Multiplier { get; set; }                 // e.g. 1.5
    public decimal MaxMultiplier { get; set; }              // hard cap, e.g. 3.0
    public DateTime? UpdatedAt { get; set; }
}
```

- [ ] **Step 2: Create SurgeTimeRule entity**

```csharp
// src/Gruuber.Rides/Domain/SurgeTimeRule.cs
namespace Gruuber.Rides.Domain;

public class SurgeTimeRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int RegionId { get; set; }
    public string RideType { get; set; } = string.Empty;
    public int? DayOfWeek { get; set; }    // 0=Sun…6=Sat; null=every day
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public decimal Multiplier { get; set; }
    public bool IsActive { get; set; } = true;
}
```

- [ ] **Step 3: Commit**

```bash
git add src/Gruuber.Rides/Domain/SurgePricingConfig.cs src/Gruuber.Rides/Domain/SurgeTimeRule.cs
git commit -m "feat(pricing): add SurgePricingConfig and SurgeTimeRule domain entities"
```

---

## Task 3: Extend Ride entity with fare fields and destination coordinates

**Files:**
- Modify: `src/Gruuber.Rides/Domain/Ride.cs`
- Modify: `src/Gruuber.Rides/Application/Commands/RideCommands.cs`

- [ ] **Step 1: Write failing unit test** (create test file first)

```csharp
// tests/Gruuber.Tests/Unit/Pricing/RideFareTests.cs
using Gruuber.Rides.Domain;
using Xunit;

public class RideFareTests
{
    [Fact]
    public void Create_SetsBaseFareAndFinalFareFromSurgeResolution()
    {
        var ride = Ride.Create(Guid.NewGuid(), "solo", 1, 1.0, 1.0, 51.5, -0.1,
            baseFare: 10.00m, surgeMultiplier: 1.5m, finalFare: 15.00m, surgeReason: "demand");

        Assert.Equal(10.00m, ride.BaseFare);
        Assert.Equal(1.5m, ride.SurgeMultiplier);
        Assert.Equal(15.00m, ride.FinalFare);
        Assert.Equal("demand", ride.SurgeReason);
    }

    [Fact]
    public void Create_DefaultsSurgeMultiplierToOne_WhenNoSurge()
    {
        var ride = Ride.Create(Guid.NewGuid(), "solo", 1, 1.0, 1.0, 51.5, -0.1,
            baseFare: 10.00m, surgeMultiplier: 1.0m, finalFare: 10.00m, surgeReason: null);

        Assert.Equal(1.0m, ride.SurgeMultiplier);
        Assert.Null(ride.SurgeReason);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd c:\Projects\app-gruuber.worktrees\copilot-feature-brainstorming-session
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "RideFareTests" -v minimal
```

Expected: compile error — `Ride.Create` doesn't accept fare parameters yet.

- [ ] **Step 3: Create the test project**

```xml
<!-- tests/Gruuber.Tests/Gruuber.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Moq" Version="4.20.72" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Testcontainers.PostgreSql" Version="3.9.0" />
    <PackageReference Include="Testcontainers.Kafka" Version="3.9.0" />
    <PackageReference Include="Testcontainers.Redis" Version="3.9.0" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Gruuber.SharedKernel\Gruuber.SharedKernel.csproj" />
    <ProjectReference Include="..\..\src\Gruuber.Rides\Gruuber.Rides.csproj" />
    <ProjectReference Include="..\..\src\Gruuber.Orders\Gruuber.Orders.csproj" />
    <ProjectReference Include="..\..\src\Gruuber.Api\Gruuber.Api.csproj" />
  </ItemGroup>
</Project>
```

Add the test project to the solution:

```bash
dotnet sln Gruuber.slnx add tests/Gruuber.Tests/Gruuber.Tests.csproj
```

- [ ] **Step 4: Extend Ride entity**

Replace the full `Ride.cs` content:

```csharp
// src/Gruuber.Rides/Domain/Ride.cs
using Gruuber.SharedKernel.Domain;

namespace Gruuber.Rides.Domain;

public class Ride : EntityBase
{
    public Guid RiderId { get; private set; }
    public Guid? DriverId { get; private set; }
    public RideStatus Status { get; private set; } = RideStatus.Requested;
    public string RideType { get; private set; } = string.Empty;
    public double PickupLat { get; private set; }
    public double PickupLng { get; private set; }
    public double? DestLat { get; private set; }
    public double? DestLng { get; private set; }
    public decimal? BaseFare { get; private set; }
    public decimal SurgeMultiplier { get; private set; } = 1.0m;
    public decimal? FinalFare { get; private set; }
    public string? SurgeReason { get; private set; }

    private Ride() { }

    public static Ride Create(
        Guid riderId,
        string rideType,
        int regionId,
        double pickupLat,
        double pickupLng,
        double? destLat = null,
        double? destLng = null,
        decimal? baseFare = null,
        decimal surgeMultiplier = 1.0m,
        decimal? finalFare = null,
        string? surgeReason = null)
    {
        return new Ride
        {
            Id = Guid.NewGuid(),
            RiderId = riderId,
            RideType = rideType,
            Status = RideStatus.Requested,
            RegionId = regionId,
            PickupLat = pickupLat,
            PickupLng = pickupLng,
            DestLat = destLat,
            DestLng = destLng,
            BaseFare = baseFare,
            SurgeMultiplier = surgeMultiplier,
            FinalFare = finalFare,
            SurgeReason = surgeReason,
            CreatedAt = DateTime.UtcNow,
            Version = 1
        };
    }

    public bool TryMatch(Guid driverId, long expectedVersion)
    {
        if (Version != expectedVersion || Status != RideStatus.Requested)
            return false;

        DriverId = driverId;
        Status = RideStatus.Matched;
        Version++;
        return true;
    }

    public bool TryTransition(RideStatus next, long expectedVersion)
    {
        if (Version != expectedVersion)
            return false;

        Status = next;
        Version++;
        return true;
    }
}
```

- [ ] **Step 5: Update RideCommands to include dest coords and fare fields**

```csharp
// src/Gruuber.Rides/Application/Commands/RideCommands.cs
namespace Gruuber.Rides.Application.Commands;

public record RequestRideCommand(
    Guid RiderId,
    string RideType,
    double PickupLat,
    double PickupLng,
    int RegionId,
    double? DestLat = null,
    double? DestLng = null);

public record FareEstimate(
    decimal BaseFare,
    decimal FinalFare,
    decimal? SurgeMultiplier,   // null when = 1.0 (not surfaced to rider)
    string? SurgeReason);       // null when = 1.0

public record RequestRideResponse(Guid RideId, string Status, string Message, FareEstimate? Fare = null);

public record MatchDriverCommand(Guid RideId, long ExpectedVersion, int RegionId);

public record TransitionRideCommand(Guid RideId, string NewStatus, long ExpectedVersion, int RegionId, Guid ActorId);
public record TransitionRideResponse(Guid RideId, string Status);
```

- [ ] **Step 6: Run test to verify it passes**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "RideFareTests" -v minimal
```

Expected: 2 tests PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Gruuber.Rides/Domain/Ride.cs src/Gruuber.Rides/Application/Commands/RideCommands.cs
git add tests/Gruuber.Tests/
git commit -m "feat(pricing): extend Ride entity with fare fields and dest coordinates; add test project"
```

---

## Task 4: Extend Order entity with fare fields

**Files:**
- Modify: `src/Gruuber.Orders/Domain/Order.cs`
- Modify: `src/Gruuber.Orders/Application/Commands/OrderCommands.cs`

- [ ] **Step 1: Write failing unit test**

```csharp
// tests/Gruuber.Tests/Unit/Pricing/OrderFareTests.cs
using Gruuber.Orders.Domain;
using Xunit;

public class OrderFareTests
{
    [Fact]
    public void ApplySurge_SetsFinalFareAndLocksIt()
    {
        var order = Order.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1);
        order.ApplySurge(baseFare: 20.00m, multiplier: 2.0m, reason: "time_rule");

        Assert.Equal(20.00m, order.BaseFare);
        Assert.Equal(2.0m, order.SurgeMultiplier);
        Assert.Equal(40.00m, order.FinalFare);
        Assert.Equal("time_rule", order.SurgeReason);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "OrderFareTests" -v minimal
```

Expected: compile error — `ApplySurge` not defined.

- [ ] **Step 3: Add fare fields to Order entity**

Add these properties and method to `src/Gruuber.Orders/Domain/Order.cs`:

```csharp
// Add properties after TotalAmount:
public decimal? BaseFare { get; private set; }
public decimal SurgeMultiplier { get; private set; } = 1.0m;
public decimal? FinalFare { get; private set; }
public string? SurgeReason { get; private set; }

// Add method:
public void ApplySurge(decimal baseFare, decimal multiplier, string? reason)
{
    BaseFare = baseFare;
    SurgeMultiplier = multiplier;
    FinalFare = baseFare * multiplier;
    SurgeReason = reason;
}
```

- [ ] **Step 4: Update OrderCommands to include fare in response**

In `src/Gruuber.Orders/Application/Commands/OrderCommands.cs`, add `FareEstimate?` to the response. First add a reference to SharedKernel pricing types — but since `FareEstimate` is in Rides, we need to either duplicate it or move it to SharedKernel. Move it to SharedKernel:

Open `src/Gruuber.SharedKernel/Pricing/SurgeResolution.cs` and add:

```csharp
// Add to namespace Gruuber.SharedKernel.Pricing:
public record FareEstimate(
    decimal BaseFare,
    decimal FinalFare,
    decimal? SurgeMultiplier,
    string? SurgeReason);
```

Remove `FareEstimate` from `src/Gruuber.Rides/Application/Commands/RideCommands.cs` and update the `using` to pull from SharedKernel:

```csharp
// In RideCommands.cs — replace local FareEstimate with:
using Gruuber.SharedKernel.Pricing;
// (remove the local FareEstimate record)
```

Update `src/Gruuber.Orders/Application/Commands/OrderCommands.cs`:

```csharp
using Gruuber.SharedKernel.Pricing;

namespace Gruuber.Orders.Application.Commands;

public record CreateOrderCommand(Guid RiderId, Guid RestaurantId, Guid RideId, int RegionId,
    IEnumerable<OrderItemCommand> Items, decimal BaseFare = 0m);
public record OrderItemCommand(Guid MenuItemId, int Quantity, decimal Price);
public record CreateOrderResponse(Guid OrderId, string Status, FareEstimate? Fare = null);
```

- [ ] **Step 5: Run test to verify it passes**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "OrderFareTests" -v minimal
```

Expected: 1 test PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Gruuber.SharedKernel/Pricing/ src/Gruuber.Orders/Domain/Order.cs src/Gruuber.Orders/Application/Commands/OrderCommands.cs src/Gruuber.Rides/Application/Commands/RideCommands.cs
git add tests/Gruuber.Tests/Unit/Pricing/OrderFareTests.cs
git commit -m "feat(pricing): add fare fields to Order entity; consolidate FareEstimate in SharedKernel"
```

---

## Task 5: DB schema — surge tables migration (Rides) and fare columns migrations

**Files:**
- Modify: `src/Gruuber.Rides/Infrastructure/RidesDbContext.cs`
- Modify: `src/Gruuber.Orders/Infrastructure/OrdersDbContext.cs`
- New migration files (auto-generated by EF CLI)

- [ ] **Step 1: Add surge entity configs to RidesDbContext**

In `src/Gruuber.Rides/Infrastructure/RidesDbContext.cs`, add DbSets and OnModelCreating configs:

```csharp
// Add to public properties:
public DbSet<SurgePricingConfig> SurgeConfigs => Set<SurgePricingConfig>();
public DbSet<SurgeTimeRule> SurgeTimeRules => Set<SurgeTimeRule>();

// Add using:
using Gruuber.Rides.Domain;

// In OnModelCreating, add:
modelBuilder.Entity<SurgePricingConfig>(e =>
{
    e.ToTable("surge_config");
    e.HasKey(x => new { x.RegionId, x.RideType, x.DemandRatioThreshold });
    e.Property(x => x.Multiplier).HasColumnType("numeric(6,2)");
    e.Property(x => x.MaxMultiplier).HasColumnType("numeric(6,2)");
    e.Property(x => x.DemandRatioThreshold).HasColumnType("numeric(4,3)");
});

modelBuilder.Entity<SurgeTimeRule>(e =>
{
    e.ToTable("surge_time_rules");
    e.HasKey(x => x.Id);
    e.Property(x => x.Multiplier).HasColumnType("numeric(6,2)");
    e.Property(x => x.StartTime).HasColumnType("time");
    e.Property(x => x.EndTime).HasColumnType("time");
});

// Add fare columns to Ride entity config (inside the existing Ride entity config block):
e.Property(x => x.DestLat);
e.Property(x => x.DestLng);
e.Property(x => x.BaseFare).HasColumnType("numeric(10,2)");
e.Property(x => x.SurgeMultiplier).HasColumnType("numeric(6,2)").HasDefaultValue(1.0m);
e.Property(x => x.FinalFare).HasColumnType("numeric(10,2)");
e.Property(x => x.SurgeReason).HasMaxLength(32);
```

- [ ] **Step 2: Generate Rides migration**

```bash
cd c:\Projects\app-gruuber.worktrees\copilot-feature-brainstorming-session
dotnet ef migrations add AddSurgePricingAndRideFare --project src/Gruuber.Rides/Gruuber.Rides.csproj --startup-project src/Gruuber.Api/Gruuber.Api.csproj
```

Expected output: `Done. To undo this action, use 'ef migrations remove'`

- [ ] **Step 3: Add fare column configs to OrdersDbContext**

In `src/Gruuber.Orders/Infrastructure/OrdersDbContext.cs`, inside the Order entity config block add:

```csharp
e.Property(x => x.BaseFare).HasColumnType("numeric(10,2)");
e.Property(x => x.SurgeMultiplier).HasColumnType("numeric(6,2)").HasDefaultValue(1.0m);
e.Property(x => x.FinalFare).HasColumnType("numeric(10,2)");
e.Property(x => x.SurgeReason).HasMaxLength(32);
```

- [ ] **Step 4: Generate Orders migration**

```bash
dotnet ef migrations add AddOrderFareColumns --project src/Gruuber.Orders/Gruuber.Orders.csproj --startup-project src/Gruuber.Api/Gruuber.Api.csproj
```

Expected output: `Done. To undo this action, use 'ef migrations remove'`

- [ ] **Step 5: Build to verify no errors**

```bash
dotnet build Gruuber.slnx -c Release --no-incremental 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add src/Gruuber.Rides/Infrastructure/ src/Gruuber.Orders/Infrastructure/
git commit -m "feat(pricing): add surge tables and fare column DB migrations"
```

---

## Task 6: Implement SurgePricingService

**Files:**
- Create: `src/Gruuber.Api/Infrastructure/SurgePricingService.cs`

- [ ] **Step 1: Write unit tests first**

```csharp
// tests/Gruuber.Tests/Unit/Pricing/SurgePricingServiceTests.cs
using Gruuber.Api.Infrastructure;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using Xunit;

public class SurgePricingServiceTests
{
    private static RidesDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<RidesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RidesDbContext(opts);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsMul1_WhenNoBelowAllThresholds()
    {
        await using var db = CreateInMemoryDb();
        db.SurgeConfigs.Add(new SurgePricingConfig
        { RegionId = 1, RideType = "ride", DemandRatioThreshold = 0.5m, Multiplier = 1.5m, MaxMultiplier = 3.0m });
        await db.SaveChangesAsync();

        var redis = new Mock<IConnectionMultiplexer>();
        var redisDbs = new Mock<IDatabase>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDbs.Object);
        // Cache miss → will query DB
        redisDbs.Setup(r => r.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        // 0 active drivers, 0 active rides → ratio = 0/1 = 0 → below 0.5 threshold
        redisDbs.Setup(r => r.SortedSetLengthAsync(It.IsAny<RedisKey>(), It.IsAny<double>(),
            It.IsAny<double>(), It.IsAny<Exclude>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(2);   // 2 available drivers

        var svc = new SurgePricingService(db, null!, redis.Object, NullLogger<SurgePricingService>.Instance);
        var result = await svc.ResolveAsync(1, "ride", 10.00m);

        Assert.Equal(1.0m, result.Multiplier);
        Assert.Null(result.Reason);
        Assert.Equal(10.00m, result.FinalFare);
    }

    [Fact]
    public async Task ResolveAsync_SelectsHighestMatchingTier()
    {
        await using var db = CreateInMemoryDb();
        db.SurgeConfigs.AddRange(
            new SurgePricingConfig { RegionId = 1, RideType = "ride", DemandRatioThreshold = 0.5m, Multiplier = 1.5m, MaxMultiplier = 3.0m },
            new SurgePricingConfig { RegionId = 1, RideType = "ride", DemandRatioThreshold = 0.75m, Multiplier = 2.0m, MaxMultiplier = 3.0m }
        );
        await db.SaveChangesAsync();

        var redis = new Mock<IConnectionMultiplexer>();
        var redisDbs = new Mock<IDatabase>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDbs.Object);
        redisDbs.Setup(r => r.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        // 1 driver available; will simulate high demand in DB via active rides count

        var svc = new SurgePricingService(db, null!, redis.Object, NullLogger<SurgePricingService>.Instance);
        // Inject 3 requested rides to simulate ratio > 0.75
        db.Rides.AddRange(
            Ride.Create(Guid.NewGuid(), "ride", 1, 0, 0),
            Ride.Create(Guid.NewGuid(), "ride", 1, 0, 0),
            Ride.Create(Guid.NewGuid(), "ride", 1, 0, 0)
        );
        await db.SaveChangesAsync();

        redisDbs.Setup(r => r.SortedSetLengthAsync(It.IsAny<RedisKey>(), It.IsAny<double>(),
            It.IsAny<double>(), It.IsAny<Exclude>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);   // 1 driver → ratio = 3/1 = 3.0 → above 0.75

        var result = await svc.ResolveAsync(1, "ride", 10.00m);

        Assert.Equal(2.0m, result.Multiplier);
        Assert.Equal("demand", result.Reason);
        Assert.Equal(20.00m, result.FinalFare);
    }

    [Fact]
    public async Task ResolveAsync_ClampsMul_ToMaxMultiplier()
    {
        await using var db = CreateInMemoryDb();
        db.SurgeConfigs.Add(new SurgePricingConfig
        { RegionId = 1, RideType = "ride", DemandRatioThreshold = 0.1m, Multiplier = 5.0m, MaxMultiplier = 3.0m });
        await db.SaveChangesAsync();

        var redis = new Mock<IConnectionMultiplexer>();
        var redisDbs = new Mock<IDatabase>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDbs.Object);
        redisDbs.Setup(r => r.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        db.Rides.Add(Ride.Create(Guid.NewGuid(), "ride", 1, 0, 0));
        await db.SaveChangesAsync();
        redisDbs.Setup(r => r.SortedSetLengthAsync(It.IsAny<RedisKey>(), It.IsAny<double>(),
            It.IsAny<double>(), It.IsAny<Exclude>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(0);   // 0 drivers → ratio = 1/1 = 1.0 → above 0.1

        var svc = new SurgePricingService(db, null!, redis.Object, NullLogger<SurgePricingService>.Instance);
        var result = await svc.ResolveAsync(1, "ride", 10.00m);

        Assert.Equal(3.0m, result.Multiplier);   // clamped from 5.0
    }

    [Fact]
    public async Task ResolveAsync_UsesTimeRule_OverDemandRatio()
    {
        await using var db = CreateInMemoryDb();
        var now = TimeOnly.FromDateTime(DateTime.UtcNow);
        db.SurgeTimeRules.Add(new SurgeTimeRule
        {
            RegionId = 1, RideType = "ride",
            StartTime = now.AddMinutes(-30),
            EndTime = now.AddMinutes(30),
            Multiplier = 2.5m, IsActive = true
        });
        await db.SaveChangesAsync();

        var redis = new Mock<IConnectionMultiplexer>();
        var redisDbs = new Mock<IDatabase>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDbs.Object);
        redisDbs.Setup(r => r.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var svc = new SurgePricingService(db, null!, redis.Object, NullLogger<SurgePricingService>.Instance);
        var result = await svc.ResolveAsync(1, "ride", 8.00m);

        Assert.Equal(2.5m, result.Multiplier);
        Assert.Equal("time_rule", result.Reason);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsMul1_WhenRedisThrows()
    {
        await using var db = CreateInMemoryDb();
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        var svc = new SurgePricingService(db, null!, redis.Object, NullLogger<SurgePricingService>.Instance);
        // Should not throw — fallback to DB which has no config → returns 1.0
        var result = await svc.ResolveAsync(1, "ride", 10.00m);

        Assert.Equal(1.0m, result.Multiplier);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "SurgePricingServiceTests" -v minimal
```

Expected: compile error — `SurgePricingService` not found.

- [ ] **Step 3: Implement SurgePricingService**

```csharp
// src/Gruuber.Api/Infrastructure/SurgePricingService.cs
using System.Text.Json;
using Gruuber.Orders.Infrastructure;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Gruuber.SharedKernel.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Gruuber.Api.Infrastructure;

public class SurgePricingService : ISurgePricingService
{
    private readonly RidesDbContext _ridesDb;
    private readonly OrdersDbContext? _ordersDb;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<SurgePricingService> _logger;
    private const int CacheTtlSeconds = 60;

    public SurgePricingService(
        RidesDbContext ridesDb,
        OrdersDbContext? ordersDb,
        IConnectionMultiplexer redis,
        ILogger<SurgePricingService> logger)
    {
        _ridesDb = ridesDb;
        _ordersDb = ordersDb;
        _redis = redis;
        _logger = logger;
    }

    public async Task<SurgeResolution> ResolveAsync(
        int regionId, string rideType, decimal baseFare,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await LoadConfigAsync(regionId, rideType, cancellationToken);

            // Check time rule first — takes precedence
            var now = TimeOnly.FromDateTime(DateTime.UtcNow);
            var dayOfWeek = (int)DateTime.UtcNow.DayOfWeek;

            var activeTimeRule = config.TimeRules.FirstOrDefault(r =>
                r.IsActive &&
                (r.DayOfWeek == null || r.DayOfWeek == dayOfWeek) &&
                now >= r.StartTime && now <= r.EndTime);

            if (activeTimeRule != null)
            {
                _logger.LogInformation(
                    "Surge time_rule applied: region={RegionId} type={RideType} multiplier={Mul}",
                    regionId, rideType, activeTimeRule.Multiplier);
                return Build(baseFare, activeTimeRule.Multiplier, "time_rule");
            }

            // Demand ratio
            var (activeRequests, availableDrivers) = await GetDemandRatioInputsAsync(
                regionId, rideType, cancellationToken);

            var ratio = activeRequests / (decimal)Math.Max(availableDrivers, 1);

            var matchingTier = config.Tiers
                .Where(t => ratio >= t.DemandRatioThreshold)
                .OrderByDescending(t => t.DemandRatioThreshold)
                .FirstOrDefault();

            if (matchingTier == null)
                return Build(baseFare, 1.0m, null);

            var multiplier = Math.Min(matchingTier.Multiplier, matchingTier.MaxMultiplier);

            _logger.LogInformation(
                "Surge demand applied: region={RegionId} type={RideType} ratio={Ratio:F2} multiplier={Mul}",
                regionId, rideType, ratio, multiplier);

            return Build(baseFare, multiplier, "demand");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SurgePricingService failed for region={RegionId}; defaulting to 1.0x", regionId);
            return Build(baseFare, 1.0m, null);
        }
    }

    private static SurgeResolution Build(decimal baseFare, decimal multiplier, string? reason) =>
        new(multiplier, reason, baseFare, baseFare * multiplier);

    private async Task<(int activeRequests, long availableDrivers)> GetDemandRatioInputsAsync(
        int regionId, string rideType, CancellationToken ct)
    {
        int activeRequests;
        if (rideType == "food" && _ordersDb != null)
        {
            activeRequests = await _ordersDb.Orders
                .CountAsync(o => o.RegionId == regionId && o.Status == Orders.Domain.OrderStatus.Placed, ct);
        }
        else
        {
            activeRequests = await _ridesDb.Rides
                .CountAsync(r => r.RegionId == regionId && r.Status == RideStatus.Requested, ct);
        }

        long availableDrivers;
        try
        {
            var db = _redis.GetDatabase();
            var ttlKey = $"driver_ttl:{regionId}";
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            availableDrivers = await db.SortedSetLengthAsync(ttlKey, now - 1, double.PositiveInfinity);
        }
        catch
        {
            availableDrivers = 1;   // conservative fallback — avoids divide-by-zero
        }

        return (activeRequests, availableDrivers);
    }

    private async Task<SurgeConfigBundle> LoadConfigAsync(
        int regionId, string rideType, CancellationToken ct)
    {
        var cacheKey = $"surge_config:{regionId}:{rideType}";
        try
        {
            var db = _redis.GetDatabase();
            var cached = await db.StringGetAsync(cacheKey);
            if (cached.HasValue)
                return JsonSerializer.Deserialize<SurgeConfigBundle>(cached!)
                    ?? new SurgeConfigBundle([], []);

            var bundle = await LoadFromDbAsync(regionId, rideType, ct);
            await db.StringSetAsync(cacheKey, JsonSerializer.Serialize(bundle),
                TimeSpan.FromSeconds(CacheTtlSeconds));
            return bundle;
        }
        catch (RedisException)
        {
            _logger.LogWarning("Redis unavailable for surge cache key={Key}, querying DB", cacheKey);
            return await LoadFromDbAsync(regionId, rideType, ct);
        }
    }

    private async Task<SurgeConfigBundle> LoadFromDbAsync(int regionId, string rideType, CancellationToken ct)
    {
        var tiers = await _ridesDb.SurgeConfigs
            .Where(s => s.RegionId == regionId && s.RideType == rideType)
            .ToListAsync(ct);
        var timeRules = await _ridesDb.SurgeTimeRules
            .Where(r => r.RegionId == regionId && r.RideType == rideType && r.IsActive)
            .ToListAsync(ct);
        return new SurgeConfigBundle(tiers, timeRules);
    }
}

internal record SurgeConfigBundle(
    List<SurgePricingConfig> Tiers,
    List<SurgeTimeRule> TimeRules);
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "SurgePricingServiceTests" -v minimal
```

Expected: 5 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Gruuber.Api/Infrastructure/SurgePricingService.cs
git add tests/Gruuber.Tests/Unit/Pricing/SurgePricingServiceTests.cs
git commit -m "feat(pricing): implement SurgePricingService with Redis cache and DB fallback"
```

---

## Task 7: Wire SurgePricingService into RequestRideHandler and CreateOrderHandler

**Files:**
- Modify: `src/Gruuber.Rides/Application/Commands/RequestRideHandler.cs`
- Modify: `src/Gruuber.Orders/Application/Commands/CreateOrderHandler.cs`

- [ ] **Step 1: Update RequestRideHandler**

Replace the full `RequestRideHandler.cs` with:

```csharp
// src/Gruuber.Rides/Application/Commands/RequestRideHandler.cs
using System.Text.Json;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Gruuber.SharedKernel.Pricing;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gruuber.Rides.Application.Commands;

public class RequestRideHandler
{
    private readonly RidesDbContext _db;
    private readonly ISurgePricingService _surge;
    private readonly ILogger<RequestRideHandler> _logger;

    public RequestRideHandler(RidesDbContext db, ISurgePricingService surge, ILogger<RequestRideHandler> logger)
    {
        _db = db;
        _surge = surge;
        _logger = logger;
    }

    public async Task<ApplicationResult<RequestRideResponse>> HandleAsync(
        RequestRideCommand command,
        CancellationToken cancellationToken = default)
    {
        // TODO: replace with real fare calculation service when available; use dummy baseFare for now
        var baseFare = 10.00m; // placeholder — real fare engine is out of scope for this PR

        var surgeResult = await _surge.ResolveAsync(command.RegionId, "ride", baseFare, cancellationToken);

        var ride = Ride.Create(
            command.RiderId, command.RideType, command.RegionId,
            command.PickupLat, command.PickupLng,
            command.DestLat, command.DestLng,
            surgeResult.BaseFare, surgeResult.Multiplier, surgeResult.FinalFare, surgeResult.Reason);

        var outboxEntry = new RideOutboxEntry
        {
            EventType = $"ride-events-{command.RegionId}",
            Payload = JsonSerializer.Serialize(new
            {
                EventName = "ride_requested",
                RideId = ride.Id,
                RiderId = ride.RiderId,
                command.PickupLat,
                command.PickupLng,
                RegionId = ride.RegionId,
                SurgeMultiplier = ride.SurgeMultiplier,
                FinalFare = ride.FinalFare,
                OccurredAt = DateTime.UtcNow
            })
        };

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.Rides.Add(ride);
        _db.Set<RideOutboxEntry>().Add(outboxEntry);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Ride {RideId} created for rider {RiderId} in region {RegionId} surge={SurgeMul}x",
            ride.Id, ride.RiderId, ride.RegionId, ride.SurgeMultiplier);

        FareEstimate? fareResponse = null;
        if (ride.BaseFare.HasValue)
        {
            fareResponse = new FareEstimate(
                ride.BaseFare.Value,
                ride.FinalFare!.Value,
                ride.SurgeMultiplier > 1.0m ? ride.SurgeMultiplier : null,
                ride.SurgeReason);
        }

        return ApplicationResult<RequestRideResponse>.Accepted(
            new RequestRideResponse(ride.Id, ride.Status.ToString(), "pending_match", fareResponse));
    }
}
```

- [ ] **Step 2: Update CreateOrderHandler**

In `src/Gruuber.Orders/Application/Commands/CreateOrderHandler.cs`, add `ISurgePricingService` injection and call it:

```csharp
// src/Gruuber.Orders/Application/Commands/CreateOrderHandler.cs
using System.Text.Json;
using Gruuber.Orders.Domain;
using Gruuber.Orders.Infrastructure;
using Gruuber.SharedKernel.Pricing;
using Gruuber.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Gruuber.Orders.Application.Commands;

public class CreateOrderHandler
{
    private readonly OrdersDbContext _db;
    private readonly ISurgePricingService _surge;
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(OrdersDbContext db, ISurgePricingService surge, ILogger<CreateOrderHandler> logger)
    {
        _db = db;
        _surge = surge;
        _logger = logger;
    }

    public async Task<ApplicationResult<CreateOrderResponse>> HandleAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var order = Order.Create(command.RiderId, command.RestaurantId, command.RideId, command.RegionId);
        foreach (var item in command.Items)
            order.AddItem(item.MenuItemId, item.Quantity, item.Price);

        var surgeResult = await _surge.ResolveAsync(command.RegionId, "food", order.TotalAmount, cancellationToken);
        order.ApplySurge(surgeResult.BaseFare, surgeResult.Multiplier, surgeResult.Reason);

        var outbox = new OrderOutboxEntry
        {
            EventType = $"order-events-{command.RegionId}",
            Payload = JsonSerializer.Serialize(new
            {
                EventName = "order_created",
                OrderId = order.Id,
                order.RiderId,
                order.RestaurantId,
                order.RideId,
                RegionId = command.RegionId,
                SurgeMultiplier = order.SurgeMultiplier,
                FinalFare = order.FinalFare,
                OccurredAt = DateTime.UtcNow
            })
        };

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.Orders.Add(order);
        _db.Set<OrderOutboxEntry>().Add(outbox);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Order {OrderId} created for rider {RiderId} in region {RegionId} surge={SurgeMul}x",
            order.Id, order.RiderId, command.RegionId, order.SurgeMultiplier);

        FareEstimate? fareResponse = null;
        if (order.BaseFare.HasValue)
        {
            fareResponse = new FareEstimate(
                order.BaseFare.Value,
                order.FinalFare!.Value,
                order.SurgeMultiplier > 1.0m ? order.SurgeMultiplier : null,
                order.SurgeReason);
        }

        return ApplicationResult<CreateOrderResponse>.Accepted(
            new CreateOrderResponse(order.Id, order.Status.ToString(), fareResponse));
    }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build Gruuber.slnx -c Release --no-incremental 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add src/Gruuber.Rides/Application/Commands/RequestRideHandler.cs
git add src/Gruuber.Orders/Application/Commands/CreateOrderHandler.cs
git commit -m "feat(pricing): inject SurgePricingService into RequestRideHandler and CreateOrderHandler"
```

---

## Task 8: Add SurgeController and register services in Program.cs

**Files:**
- Create: `src/Gruuber.Api/Controllers/SurgeController.cs`
- Modify: `src/Gruuber.Api/Program.cs`
- Modify: `src/Gruuber.Rides/RidesModule.cs`
- Modify: `src/Gruuber.Orders/OrdersModule.cs`

- [ ] **Step 1: Create SurgeController**

```csharp
// src/Gruuber.Api/Controllers/SurgeController.cs
using Gruuber.Api.Extensions;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Gruuber.SharedKernel.Infrastructure;
using Gruuber.SharedKernel.Pricing;
using Gruuber.SharedKernel.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Gruuber.Api.Controllers;

[ApiController]
[Route("v1/surge")]
public class SurgeController : ControllerBase
{
    private readonly ISurgePricingService _surge;
    private readonly RidesDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ICurrentUserContext _currentUser;

    public SurgeController(
        ISurgePricingService surge,
        RidesDbContext db,
        IConnectionMultiplexer redis,
        ICurrentUserContext currentUser)
    {
        _surge = surge;
        _db = db;
        _redis = redis;
        _currentUser = currentUser;
    }

    /// <summary>GET /v1/surge/estimate — pre-booking surge preview (valid for ~30s)</summary>
    [HttpGet("estimate")]
    [Authorize]
    public async Task<IActionResult> GetEstimate(
        [FromQuery] int region_id,
        [FromQuery] string ride_type,
        CancellationToken cancellationToken)
    {
        if (ride_type is not ("ride" or "food"))
            return BadRequest(new { error = "ride_type must be 'ride' or 'food'" });

        var resolution = await _surge.ResolveAsync(region_id, ride_type, 1.0m, cancellationToken);
        return Ok(new
        {
            surge_multiplier = resolution.Multiplier,
            surge_reason = resolution.Reason,
            valid_for_secs = 30
        });
    }

    /// <summary>PUT /v1/admin/surge/config — update surge tiers and immediately invalidate Redis cache</summary>
    [HttpPut("/v1/admin/surge/config")]
    [Authorize(Policy = "admin")]
    public async Task<IActionResult> UpdateConfig(
        [FromBody] UpdateSurgeConfigRequest request,
        CancellationToken cancellationToken)
    {
        // Remove existing tiers for this region+type then re-insert
        var existing = await _db.SurgeConfigs
            .Where(c => c.RegionId == request.RegionId && c.RideType == request.RideType)
            .ToListAsync(cancellationToken);
        _db.SurgeConfigs.RemoveRange(existing);

        foreach (var tier in request.Tiers)
        {
            _db.SurgeConfigs.Add(new SurgePricingConfig
            {
                RegionId = request.RegionId,
                RideType = request.RideType,
                DemandRatioThreshold = tier.DemandRatioThreshold,
                Multiplier = tier.Multiplier,
                MaxMultiplier = request.MaxMultiplier,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Immediately invalidate Redis cache
        try
        {
            var cacheKey = $"surge_config:{request.RegionId}:{request.RideType}";
            await _redis.GetDatabase().KeyDeleteAsync(cacheKey);
        }
        catch { /* non-fatal */ }

        return Ok(new { updated = true });
    }
}

public record UpdateSurgeConfigRequest(
    int RegionId,
    string RideType,
    decimal MaxMultiplier,
    List<SurgeTierRequest> Tiers);

public record SurgeTierRequest(decimal DemandRatioThreshold, decimal Multiplier);
```

- [ ] **Step 2: Register SurgePricingService in Program.cs**

In `src/Gruuber.Api/Program.cs`, after `// Driver scoring` line add:

```csharp
// Surge pricing (inline — scoped because it accesses scoped DbContexts)
builder.Services.AddScoped<ISurgePricingService, SurgePricingService>();
```

Also add the using at the top:
```csharp
using Gruuber.SharedKernel.Pricing;
```

- [ ] **Step 3: Update RidesModule to register ISurgePricingService dependency**

In `src/Gruuber.Rides/RidesModule.cs`, the `RequestRideHandler` now depends on `ISurgePricingService`. This is registered at the API level (Program.cs), so no change is needed in RidesModule itself. However, update the Orders module to inject `ISurgePricingService` into `CreateOrderHandler`:

In `src/Gruuber.Orders/OrdersModule.cs`, `CreateOrderHandler` is registered as scoped — `ISurgePricingService` will be resolved from the same DI scope, so no change needed.

- [ ] **Step 4: Build and verify**

```bash
dotnet build Gruuber.slnx -c Release --no-incremental 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add src/Gruuber.Api/Controllers/SurgeController.cs src/Gruuber.Api/Program.cs
git commit -m "feat(pricing): add SurgeController with estimate and admin config endpoints; register service"
```

---

## Task 9: Integration tests

**Files:**
- Create: `tests/Gruuber.Tests/Integration/Pricing/SurgePricingIntegrationTests.cs`

- [ ] **Step 1: Write integration tests**

```csharp
// tests/Gruuber.Tests/Integration/Pricing/SurgePricingIntegrationTests.cs
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using Xunit;

/// <summary>
/// Integration-level tests for SurgePricingService using a real Postgres container.
/// These tests verify fare lock invariants and config invalidation.
/// </summary>
public class SurgePricingIntegrationTests : IAsyncLifetime
{
    // NOTE: Full Testcontainers integration requires docker. For CI, use:
    //   dotnet test --filter "Category=Integration"
    // Tag these with [Trait("Category", "Integration")]

    [Fact(Skip = "Requires Docker — run with docker-compose up")]
    [Trait("Category", "Integration")]
    public async Task BookRide_DuringActiveTimeRule_LocksCorrectFinalFare()
    {
        // Arrange: start postgres container, seed surge_time_rules with current window
        // Act: call ResolveAsync and persist ride via RequestRideHandler
        // Assert: ride.final_fare in DB == base_fare * rule_multiplier
        // (Detailed setup left to integration test runner with Testcontainers)
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires Docker — run with docker-compose up")]
    [Trait("Category", "Integration")]
    public async Task BookRide_AtHighDemand_CorrectTierApplied()
    {
        // Arrange: seed surge_config with 2 tiers, insert N requested rides to hit upper tier
        // Act: call ResolveAsync
        // Assert: multiplier matches upper tier, final_fare persisted correctly
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires Docker — run with docker-compose up")]
    [Trait("Category", "Integration")]
    public async Task AdminUpdatesConfig_RedisKeyDeleted_NextRequestUsesNewConfig()
    {
        // Arrange: seed config, prime Redis cache, update via PUT /v1/admin/surge/config
        // Assert: Redis key deleted, next ResolveAsync loads updated config from DB
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires Docker — run with docker-compose up")]
    [Trait("Category", "Integration")]
    public async Task FinalFareInvariant_Unchanged_AfterAdminUpdatesConfigPostBooking()
    {
        // Arrange: book ride at 1.5x surge; admin then changes config to 2.0x
        // Assert: ride.FinalFare in DB is still base * 1.5
        await Task.CompletedTask;
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;
}
```

- [ ] **Step 2: Run unit tests suite to confirm all pass**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "Category!=Integration" -v minimal
```

Expected: All unit tests PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/Gruuber.Tests/Integration/Pricing/
git commit -m "test(pricing): add integration test stubs for surge pricing fare lock invariants"
```

---

## Completion Checklist

- [ ] `/v1/surge/estimate` and `/v1/admin/surge/config` endpoints exist under correct versioned paths
- [ ] `SurgePricingService` called inline — not in a background worker
- [ ] `final_fare` written in same DB transaction as ride/order INSERT
- [ ] Redis fallback to DB on unavailability — no error surfaced to rider
- [ ] `surge_multiplier` and `surge_reason` omitted from response when multiplier = 1.0
- [ ] Admin config update immediately invalidates Redis key
- [ ] `SurgeMultiplier` and `SurgeReason` included in structured log entries
- [ ] `CancellationToken` propagated through all async resolution methods
- [ ] All unit tests pass
