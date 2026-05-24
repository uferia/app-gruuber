# Ride Pooling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add ride pooling as a new ride type — up to 2 riders share a trip with a discounted fare, matched in real-time via a Redis sorted-set queue and a dedicated Kafka consumer (`PoolMatcherService`).

**Architecture:** Pool rides extend the existing `rides` table (new columns: `pool_trip_id`, `pool_slot`, `dest_lat`, `dest_lng`). A new `pool_region_rates` table drives per-region config. `RequestRideHandler` pushes pool requests to a Redis sorted set and emits `ride_pool_queued` to the outbox. `PoolMatcherService` (Kafka consumer + `BackgroundService`) atomically matches pairs using a Lua script, then runs the existing driver scoring pipeline filtered to pool-capable drivers. A `PoolTimeoutWorker` sweeps for expired queued rides every 30s and emits upgrade/cancel prompts. Rider privacy is enforced: no other rider's coords are ever stored or returned.

**Tech Stack:** ASP.NET Core 8, EF Core 8, Npgsql, StackExchange.Redis, Confluent.Kafka, xunit, Moq, Testcontainers

---

## File Map

**New files:**
- `src/Gruuber.Rides/Domain/RidePool.cs` — `PoolRegionRate` entity
- `src/Gruuber.Rides/Application/Commands/RequestPoolRideHandler.cs` — pool-specific logic, extracted from `RequestRideHandler`
- `src/Gruuber.Rides/Application/Commands/AcceptSoloUpgradeHandler.cs`
- `src/Gruuber.Rides/Application/PoolMatcherService.cs` — Kafka consumer BackgroundService
- `src/Gruuber.Rides/Application/PoolTimeoutWorker.cs` — sweep BackgroundService
- `tests/Gruuber.Tests/Unit/Pool/PoolMatcherServiceTests.cs`
- `tests/Gruuber.Tests/Unit/Pool/PoolTimeoutWorkerTests.cs`
- `tests/Gruuber.Tests/Integration/Pool/RidePoolingIntegrationTests.cs`

**Modified files:**
- `src/Gruuber.Rides/Domain/RideStatus.cs` — add `PoolQueued`, `PoolMatched`, `PartialDropoff`
- `src/Gruuber.Rides/Domain/Ride.cs` — add `PoolTripId`, `PoolSlot`, `DestLat`, `DestLng` (if not already added by Surge Pricing plan)
- `src/Gruuber.Rides/Application/Commands/RideCommands.cs` — add pool fields to command/response
- `src/Gruuber.Rides/Application/Commands/RequestRideHandler.cs` — branch to pool logic when `ride_type=pool`
- `src/Gruuber.Rides/Infrastructure/RidesDbContext.cs` — add `PoolRegionRate` entity config
- `src/Gruuber.Rides/RidesModule.cs` — register new handlers and services
- `src/Gruuber.Api/Controllers/RidesController.cs` — add `accept-solo-upgrade` endpoint
- `src/Gruuber.Api/Program.cs` — register `PoolMatcherService` and `PoolTimeoutWorker`
- EF migration for `Gruuber.Rides`

---

## Task 1: Extend RideStatus enum with pool states

**Files:**
- Modify: `src/Gruuber.Rides/Domain/RideStatus.cs`

- [ ] **Step 1: Write failing test**

```csharp
// tests/Gruuber.Tests/Unit/Pool/RidePoolStatusTests.cs
using Gruuber.Rides.Domain;
using Xunit;

public class RidePoolStatusTests
{
    [Fact]
    public void RideStatus_HasPoolStates()
    {
        // These must compile and be distinct
        _ = RideStatus.PoolQueued;
        _ = RideStatus.PoolMatched;
        _ = RideStatus.PartialDropoff;
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd c:\Projects\app-gruuber.worktrees\copilot-feature-brainstorming-session
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "RidePoolStatusTests" -v minimal
```

Expected: compile error — enum members do not exist yet.

- [ ] **Step 3: Add pool states to RideStatus enum**

Replace `src/Gruuber.Rides/Domain/RideStatus.cs`:

```csharp
namespace Gruuber.Rides.Domain;

public enum RideStatus
{
    Requested,
    PoolQueued,     // waiting in Redis queue for a pool match
    PoolMatched,    // paired with another rider; driver matching pending
    Matched,
    EnRoute,
    Arrived,
    PartialDropoff, // Rider 1 dropped off; en route to Rider 2
    Completed,
    Cancelled
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "RidePoolStatusTests" -v minimal
```

Expected: 1 test PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Gruuber.Rides/Domain/RideStatus.cs tests/Gruuber.Tests/Unit/Pool/RidePoolStatusTests.cs
git commit -m "feat(pool): add PoolQueued, PoolMatched, PartialDropoff to RideStatus enum"
```

---

## Task 2: Extend Ride entity with pool fields

**Files:**
- Modify: `src/Gruuber.Rides/Domain/Ride.cs`

> **Note:** If the Surge Pricing plan was implemented first, `DestLat` and `DestLng` are already on the entity. Skip those properties below; only add the pool-specific ones.

- [ ] **Step 1: Write failing test**

```csharp
// tests/Gruuber.Tests/Unit/Pool/RidePoolEntityTests.cs
using Gruuber.Rides.Domain;
using Xunit;

public class RidePoolEntityTests
{
    [Fact]
    public void CreatePool_SetsPoolStatusAndRideType()
    {
        var ride = Ride.CreatePool(Guid.NewGuid(), regionId: 1,
            pickupLat: 51.5, pickupLng: -0.1, destLat: 51.6, destLng: -0.05);

        Assert.Equal(RideStatus.PoolQueued, ride.Status);
        Assert.Equal("pool", ride.RideType);
        Assert.Null(ride.PoolTripId);
        Assert.Null(ride.PoolSlot);
    }

    [Fact]
    public void AssignPool_SetsPoolTripIdAndSlot_AndTransitionsToPoolMatched()
    {
        var ride = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        var tripId = Guid.NewGuid();

        var ok = ride.TryAssignPool(tripId, slot: 1, expectedVersion: 1);

        Assert.True(ok);
        Assert.Equal(tripId, ride.PoolTripId);
        Assert.Equal(1, ride.PoolSlot);
        Assert.Equal(RideStatus.PoolMatched, ride.Status);
        Assert.Equal(2, ride.Version);
    }

    [Fact]
    public void AssignPool_ReturnsFalse_OnVersionMismatch()
    {
        var ride = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        var ok = ride.TryAssignPool(Guid.NewGuid(), slot: 1, expectedVersion: 99);
        Assert.False(ok);
    }

    [Fact]
    public void UpgradeToSolo_TransitionsPoolQueuedToRequested()
    {
        var ride = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        var ok = ride.TryUpgradeToSolo(expectedVersion: 1);

        Assert.True(ok);
        Assert.Equal(RideStatus.Requested, ride.Status);
        Assert.Equal("solo", ride.RideType);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "RidePoolEntityTests" -v minimal
```

Expected: compile errors — `CreatePool`, `TryAssignPool`, `TryUpgradeToSolo`, `PoolTripId`, `PoolSlot` not defined.

- [ ] **Step 3: Add pool fields and methods to Ride entity**

Add to `src/Gruuber.Rides/Domain/Ride.cs` (inside the class, after existing properties):

```csharp
// Pool-specific properties
public Guid? PoolTripId { get; private set; }
public int? PoolSlot { get; private set; }

// Dest coords (add these only if not already present from surge pricing plan)
public double? DestLat { get; private set; }
public double? DestLng { get; private set; }
```

Add these factory and mutation methods:

```csharp
/// <summary>Creates a pool ride in PoolQueued status.</summary>
public static Ride CreatePool(
    Guid riderId, int regionId,
    double pickupLat, double pickupLng,
    double destLat, double destLng,
    decimal? baseFare = null, decimal surgeMultiplier = 1.0m, decimal? finalFare = null)
{
    return new Ride
    {
        Id = Guid.NewGuid(),
        RiderId = riderId,
        RideType = "pool",
        Status = RideStatus.PoolQueued,
        RegionId = regionId,
        PickupLat = pickupLat,
        PickupLng = pickupLng,
        DestLat = destLat,
        DestLng = destLng,
        BaseFare = baseFare,
        SurgeMultiplier = surgeMultiplier,
        FinalFare = finalFare,
        CreatedAt = DateTime.UtcNow,
        Version = 1
    };
}

/// <summary>
/// Transitions PoolQueued → PoolMatched and assigns pool trip.
/// Returns false on version mismatch (caller should retry with fresh version).
/// </summary>
public bool TryAssignPool(Guid poolTripId, int slot, long expectedVersion)
{
    if (Version != expectedVersion || Status != RideStatus.PoolQueued)
        return false;

    PoolTripId = poolTripId;
    PoolSlot = slot;
    Status = RideStatus.PoolMatched;
    Version++;
    return true;
}

/// <summary>
/// Upgrades a timed-out PoolQueued ride to solo Requested.
/// Returns false on version mismatch.
/// </summary>
public bool TryUpgradeToSolo(long expectedVersion)
{
    if (Version != expectedVersion || Status != RideStatus.PoolQueued)
        return false;

    RideType = "solo";
    Status = RideStatus.Requested;
    PoolTripId = null;
    PoolSlot = null;
    Version++;
    return true;
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "RidePoolEntityTests" -v minimal
```

Expected: 4 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Gruuber.Rides/Domain/Ride.cs tests/Gruuber.Tests/Unit/Pool/RidePoolEntityTests.cs
git commit -m "feat(pool): add pool fields and pool lifecycle methods to Ride entity"
```

---

## Task 3: PoolRegionRate entity and DB migration

**Files:**
- Create: `src/Gruuber.Rides/Domain/RidePool.cs`
- Modify: `src/Gruuber.Rides/Infrastructure/RidesDbContext.cs`
- New EF migration

- [ ] **Step 1: Create PoolRegionRate entity**

```csharp
// src/Gruuber.Rides/Domain/RidePool.cs
namespace Gruuber.Rides.Domain;

public class PoolRegionRate
{
    public int RegionId { get; set; }
    public decimal DiscountPct { get; set; }           // e.g. 0.20 = 20% off
    public int MatchTimeoutSecs { get; set; } = 120;
    public decimal MaxDetourKm { get; set; } = 2.0m;
    public DateTime? UpdatedAt { get; set; }
}
```

- [ ] **Step 2: Register in RidesDbContext**

In `src/Gruuber.Rides/Infrastructure/RidesDbContext.cs`:

```csharp
// Add DbSet:
public DbSet<PoolRegionRate> PoolRegionRates => Set<PoolRegionRate>();

// Add to OnModelCreating:
modelBuilder.Entity<PoolRegionRate>(e =>
{
    e.ToTable("pool_region_rates");
    e.HasKey(x => x.RegionId);
    e.Property(x => x.DiscountPct).HasColumnType("numeric(4,3)");
    e.Property(x => x.MaxDetourKm).HasColumnType("numeric(6,2)");
});

// Add pool column configs inside the Ride entity config block:
e.Property(x => x.PoolTripId);
e.Property(x => x.PoolSlot);
// DestLat/DestLng are added here if not already present from surge pricing plan
```

Also add `ride_type` column default and `pool_slot`/`pool_trip_id` to the EF config, and update `ride_views` model:

```csharp
// In RideView entity config (already exists), add:
// e.Property(x => x.RideType);   -- add RideType and PoolSlot to RideViewEntry
```

In `src/Gruuber.Rides/Infrastructure/RideView.cs`, add:
```csharp
public string? RideType { get; set; }
public int? PoolSlot { get; set; }
```

- [ ] **Step 3: Generate migration**

```bash
cd c:\Projects\app-gruuber.worktrees\copilot-feature-brainstorming-session
dotnet ef migrations add AddRidePooling --project src/Gruuber.Rides/Gruuber.Rides.csproj --startup-project src/Gruuber.Api/Gruuber.Api.csproj
```

Expected: migration file created.

- [ ] **Step 4: Build**

```bash
dotnet build Gruuber.slnx -c Release --no-incremental 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add src/Gruuber.Rides/Domain/RidePool.cs src/Gruuber.Rides/Infrastructure/
git commit -m "feat(pool): add PoolRegionRate entity, ride_views pool columns, and DB migration"
```

---

## Task 4: RequestRideHandler — pool branching and Redis queue push

**Files:**
- Modify: `src/Gruuber.Rides/Application/Commands/RideCommands.cs`
- Modify: `src/Gruuber.Rides/Application/Commands/RequestRideHandler.cs`

- [ ] **Step 1: Write failing test**

```csharp
// tests/Gruuber.Tests/Unit/Pool/RequestPoolRideHandlerTests.cs
using System.Text.Json;
using Gruuber.Rides.Application.Commands;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Gruuber.SharedKernel.Pricing;
using StackExchange.Redis;
using Xunit;

public class RequestPoolRideHandlerTests
{
    private static RidesDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<RidesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new RidesDbContext(opts);
    }

    [Fact]
    public async Task HandleAsync_PoolRide_ReturnsPoolQueuedStatus()
    {
        await using var db = CreateInMemoryDb();
        db.PoolRegionRates.Add(new PoolRegionRate
            { RegionId = 1, DiscountPct = 0.20m, MatchTimeoutSecs = 120, MaxDetourKm = 2.0m });
        await db.SaveChangesAsync();

        var redis = new Mock<IConnectionMultiplexer>();
        var redisDbs = new Mock<IDatabase>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDbs.Object);
        redisDbs.Setup(r => r.SortedSetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
            It.IsAny<double>(), It.IsAny<SortedSetWhen>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var surge = new Mock<ISurgePricingService>();
        surge.Setup(s => s.ResolveAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SurgeResolution(1.0m, null, 8.00m, 8.00m));

        var handler = new RequestRideHandler(db, surge.Object, redis.Object, NullLogger<RequestRideHandler>.Instance);
        var cmd = new RequestRideCommand(Guid.NewGuid(), "pool", 51.5, -0.1, 1, 51.6, -0.05);

        var result = await handler.HandleAsync(cmd);

        Assert.True(result.IsSuccess);
        Assert.Equal(202, result.StatusCode);
        Assert.Equal("PoolQueued", result.Data!.Status);

        var savedRide = await db.Rides.SingleAsync();
        Assert.Equal(RideStatus.PoolQueued, savedRide.Status);
    }

    [Fact]
    public async Task HandleAsync_PoolRide_ReturnsError_WhenNoRegionRate()
    {
        await using var db = CreateInMemoryDb();
        // No PoolRegionRate seeded for region 1
        var redis = new Mock<IConnectionMultiplexer>();
        var surge = new Mock<ISurgePricingService>();
        surge.Setup(s => s.ResolveAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SurgeResolution(1.0m, null, 8.00m, 8.00m));

        var handler = new RequestRideHandler(db, surge.Object, redis.Object, NullLogger<RequestRideHandler>.Instance);
        var cmd = new RequestRideCommand(Guid.NewGuid(), "pool", 51.5, -0.1, 1, 51.6, -0.05);

        var result = await handler.HandleAsync(cmd);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("POOL_NOT_AVAILABLE", result.ErrorCode);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "RequestPoolRideHandlerTests" -v minimal
```

Expected: compile errors (handler signature doesn't match yet).

- [ ] **Step 3: Update RideCommands**

In `src/Gruuber.Rides/Application/Commands/RideCommands.cs`, update `RequestRideCommand` and `RequestRideResponse` to include pool fields:

```csharp
using Gruuber.SharedKernel.Pricing;

namespace Gruuber.Rides.Application.Commands;

public record RequestRideCommand(
    Guid RiderId,
    string RideType,          // "solo" | "pool"
    double PickupLat,
    double PickupLng,
    int RegionId,
    double? DestLat = null,
    double? DestLng = null);

public record RequestRideResponse(
    Guid RideId,
    string Status,
    string Message,
    FareEstimate? Fare = null,
    int? MatchTimeoutSecs = null,           // pool only
    decimal? DiscountedFareEstimate = null); // pool only

public record MatchDriverCommand(Guid RideId, long ExpectedVersion, int RegionId);
public record TransitionRideCommand(Guid RideId, string NewStatus, long ExpectedVersion, int RegionId, Guid ActorId);
public record TransitionRideResponse(Guid RideId, string Status);

public record AcceptSoloUpgradeCommand(Guid RideId, long ExpectedVersion, Guid RiderId, int RegionId);
public record AcceptSoloUpgradeResponse(Guid RideId, string Status);
```

- [ ] **Step 4: Update RequestRideHandler with pool branch**

Replace full `src/Gruuber.Rides/Application/Commands/RequestRideHandler.cs`:

```csharp
using System.Text.Json;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Gruuber.SharedKernel.Pricing;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Gruuber.Rides.Application.Commands;

public class RequestRideHandler
{
    private readonly RidesDbContext _db;
    private readonly ISurgePricingService _surge;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RequestRideHandler> _logger;

    public RequestRideHandler(RidesDbContext db, ISurgePricingService surge,
        IConnectionMultiplexer redis, ILogger<RequestRideHandler> logger)
    {
        _db = db;
        _surge = surge;
        _redis = redis;
        _logger = logger;
    }

    public async Task<ApplicationResult<RequestRideResponse>> HandleAsync(
        RequestRideCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.RideType == "pool")
            return await HandlePoolAsync(command, cancellationToken);

        return await HandleSoloAsync(command, cancellationToken);
    }

    private async Task<ApplicationResult<RequestRideResponse>> HandleSoloAsync(
        RequestRideCommand command, CancellationToken cancellationToken)
    {
        var baseFare = 10.00m; // placeholder — real fare engine out of scope
        var surgeResult = await _surge.ResolveAsync(command.RegionId, "ride", baseFare, cancellationToken);

        var ride = Ride.Create(command.RiderId, "solo", command.RegionId,
            command.PickupLat, command.PickupLng,
            command.DestLat, command.DestLng,
            surgeResult.BaseFare, surgeResult.Multiplier, surgeResult.FinalFare, surgeResult.Reason);

        var outboxEntry = BuildOutbox("ride_requested", command.RegionId, ride.Id, ride.RiderId,
            command.PickupLat, command.PickupLng, ride.SurgeMultiplier, ride.FinalFare);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.Rides.Add(ride);
        _db.Set<RideOutboxEntry>().Add(outboxEntry);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation("Ride {RideId} created (solo) rider={RiderId} region={RegionId}",
            ride.Id, ride.RiderId, ride.RegionId);

        return ApplicationResult<RequestRideResponse>.Accepted(
            new RequestRideResponse(ride.Id, ride.Status.ToString(), "pending_match",
                ride.BaseFare.HasValue
                    ? new FareEstimate(ride.BaseFare.Value, ride.FinalFare!.Value,
                        ride.SurgeMultiplier > 1.0m ? ride.SurgeMultiplier : null, ride.SurgeReason)
                    : null));
    }

    private async Task<ApplicationResult<RequestRideResponse>> HandlePoolAsync(
        RequestRideCommand command, CancellationToken cancellationToken)
    {
        if (command.DestLat is null || command.DestLng is null)
            return ApplicationResult<RequestRideResponse>.Failure(
                "DEST_REQUIRED", "Pool rides require destination coordinates.", 400);

        var rate = await _db.PoolRegionRates
            .FirstOrDefaultAsync(r => r.RegionId == command.RegionId, cancellationToken);

        if (rate is null)
            return ApplicationResult<RequestRideResponse>.Failure(
                "POOL_NOT_AVAILABLE", "Pool rides are not available in this region.", 400);

        var baseFare = 10.00m; // placeholder
        var discountedBase = baseFare * (1 - rate.DiscountPct);
        var surgeResult = await _surge.ResolveAsync(command.RegionId, "ride", discountedBase, cancellationToken);

        var ride = Ride.CreatePool(command.RiderId, command.RegionId,
            command.PickupLat, command.PickupLng,
            command.DestLat.Value, command.DestLng.Value,
            surgeResult.BaseFare, surgeResult.Multiplier, surgeResult.FinalFare);

        var queueEntry = JsonSerializer.Serialize(new
        {
            RideId = ride.Id,
            RiderId = ride.RiderId,
            Lat = command.PickupLat,
            Lng = command.PickupLng,
            DestLat = command.DestLat,
            DestLng = command.DestLng,
            RequestedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        var outboxEntry = new RideOutboxEntry
        {
            EventType = $"ride-events-{command.RegionId}",
            Payload = JsonSerializer.Serialize(new
            {
                EventName = "ride_pool_queued",
                RideId = ride.Id,
                RiderId = ride.RiderId,
                RegionId = ride.RegionId,
                Origin = new { Lat = command.PickupLat, Lng = command.PickupLng },
                Destination = new { Lat = command.DestLat, Lng = command.DestLng },
                RequestedAt = DateTime.UtcNow
            })
        };

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.Rides.Add(ride);
        _db.Set<RideOutboxEntry>().Add(outboxEntry);
        await _db.SaveChangesAsync(cancellationToken);

        // Push to Redis pool queue (outside DB tx — acceptable eventual push)
        var queueKey = $"pool_queue:{command.RegionId}";
        var score = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var redisDb = _redis.GetDatabase();
        await redisDb.SortedSetAddAsync(queueKey, queueEntry, score);
        await redisDb.KeyExpireAsync(queueKey, TimeSpan.FromSeconds(rate.MatchTimeoutSecs * 2));

        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Ride {RideId} created (pool) rider={RiderId} region={RegionId} timeout={Timeout}s",
            ride.Id, ride.RiderId, ride.RegionId, rate.MatchTimeoutSecs);

        return ApplicationResult<RequestRideResponse>.Accepted(
            new RequestRideResponse(ride.Id, ride.Status.ToString(), "pool_queued",
                null, rate.MatchTimeoutSecs, surgeResult.FinalFare));
    }

    private static RideOutboxEntry BuildOutbox(string eventName, int regionId, Guid rideId, Guid riderId,
        double pickupLat, double pickupLng, decimal surgeMul, decimal? finalFare) =>
        new()
        {
            EventType = $"ride-events-{regionId}",
            Payload = JsonSerializer.Serialize(new
            {
                EventName = eventName,
                RideId = rideId,
                RiderId = riderId,
                PickupLat = pickupLat,
                PickupLng = pickupLng,
                RegionId = regionId,
                SurgeMultiplier = surgeMul,
                FinalFare = finalFare,
                OccurredAt = DateTime.UtcNow
            })
        };
}
```

- [ ] **Step 5: Run tests**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "RequestPoolRideHandlerTests" -v minimal
```

Expected: 2 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Gruuber.Rides/Application/Commands/RideCommands.cs
git add src/Gruuber.Rides/Application/Commands/RequestRideHandler.cs
git add tests/Gruuber.Tests/Unit/Pool/RequestPoolRideHandlerTests.cs
git commit -m "feat(pool): extend RequestRideHandler to handle pool rides with Redis queue push"
```

---

## Task 5: AcceptSoloUpgradeHandler

**Files:**
- Create: `src/Gruuber.Rides/Application/Commands/AcceptSoloUpgradeHandler.cs`

- [ ] **Step 1: Write failing test**

```csharp
// tests/Gruuber.Tests/Unit/Pool/AcceptSoloUpgradeHandlerTests.cs
using Gruuber.Rides.Application.Commands;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class AcceptSoloUpgradeHandlerTests
{
    private static RidesDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<RidesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new RidesDbContext(opts);
    }

    [Fact]
    public async Task HandleAsync_TransitionsToRequested_AndEmitsOutboxEvent()
    {
        await using var db = CreateInMemoryDb();
        var ride = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        db.Rides.Add(ride);
        await db.SaveChangesAsync();

        var handler = new AcceptSoloUpgradeHandler(db, NullLogger<AcceptSoloUpgradeHandler>.Instance);
        var cmd = new AcceptSoloUpgradeCommand(ride.Id, expectedVersion: 1, ride.RiderId, regionId: 1);

        var result = await handler.HandleAsync(cmd);

        Assert.True(result.IsSuccess);
        Assert.Equal(202, result.StatusCode);

        var updated = await db.Rides.FindAsync(ride.Id);
        Assert.Equal(RideStatus.Requested, updated!.Status);
        Assert.Equal("solo", updated.RideType);

        var outbox = await db.Set<RideOutboxEntry>().SingleAsync();
        Assert.Contains("ride_pool_upgraded", outbox.Payload);
    }

    [Fact]
    public async Task HandleAsync_Returns404_WhenRideNotFound()
    {
        await using var db = CreateInMemoryDb();
        var handler = new AcceptSoloUpgradeHandler(db, NullLogger<AcceptSoloUpgradeHandler>.Instance);
        var result = await handler.HandleAsync(new AcceptSoloUpgradeCommand(Guid.NewGuid(), 1, Guid.NewGuid(), 1));

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task HandleAsync_Returns409_OnVersionMismatch()
    {
        await using var db = CreateInMemoryDb();
        var ride = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        db.Rides.Add(ride);
        await db.SaveChangesAsync();

        var handler = new AcceptSoloUpgradeHandler(db, NullLogger<AcceptSoloUpgradeHandler>.Instance);
        var result = await handler.HandleAsync(
            new AcceptSoloUpgradeCommand(ride.Id, expectedVersion: 99, ride.RiderId, regionId: 1));

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "AcceptSoloUpgradeHandlerTests" -v minimal
```

Expected: compile error — `AcceptSoloUpgradeHandler` not found.

- [ ] **Step 3: Implement AcceptSoloUpgradeHandler**

```csharp
// src/Gruuber.Rides/Application/Commands/AcceptSoloUpgradeHandler.cs
using System.Text.Json;
using Gruuber.Rides.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Gruuber.Rides.Application.Commands;

public class AcceptSoloUpgradeHandler
{
    private readonly RidesDbContext _db;
    private readonly ILogger<AcceptSoloUpgradeHandler> _logger;

    public AcceptSoloUpgradeHandler(RidesDbContext db, ILogger<AcceptSoloUpgradeHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ApplicationResult<AcceptSoloUpgradeResponse>> HandleAsync(
        AcceptSoloUpgradeCommand command, CancellationToken cancellationToken = default)
    {
        var ride = await _db.Rides.FindAsync([command.RideId], cancellationToken);
        if (ride is null)
            return ApplicationResult<AcceptSoloUpgradeResponse>.Failure("NOT_FOUND", "Ride not found.", 404);

        if (!ride.TryUpgradeToSolo(command.ExpectedVersion))
            return ApplicationResult<AcceptSoloUpgradeResponse>.Conflict(ride.Id, ride.Version);

        var outboxEntry = new RideOutboxEntry
        {
            EventType = $"ride-events-{command.RegionId}",
            Payload = JsonSerializer.Serialize(new
            {
                EventName = "ride_pool_upgraded",
                RideId = ride.Id,
                RiderId = ride.RiderId,
                RegionId = command.RegionId,
                PreviousStatus = "pool_queued",
                OccurredAt = DateTime.UtcNow
            })
        };

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.Set<RideOutboxEntry>().Add(outboxEntry);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation("Ride {RideId} upgraded to solo by rider {RiderId}", ride.Id, command.RiderId);

        return ApplicationResult<AcceptSoloUpgradeResponse>.Accepted(
            new AcceptSoloUpgradeResponse(ride.Id, ride.Status.ToString()));
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "AcceptSoloUpgradeHandlerTests" -v minimal
```

Expected: 3 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Gruuber.Rides/Application/Commands/AcceptSoloUpgradeHandler.cs
git add tests/Gruuber.Tests/Unit/Pool/AcceptSoloUpgradeHandlerTests.cs
git commit -m "feat(pool): add AcceptSoloUpgradeHandler with optimistic concurrency check"
```

---

## Task 6: PoolMatcherService (Kafka consumer)

**Files:**
- Create: `src/Gruuber.Rides/Application/PoolMatcherService.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/Gruuber.Tests/Unit/Pool/PoolMatcherServiceTests.cs
using Gruuber.Rides.Application;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using Xunit;

public class PoolMatcherServiceTests
{
    private static RidesDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<RidesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new RidesDbContext(opts);
    }

    [Fact]
    public async Task TryMatchRidesAsync_MatchesTwoCompatibleRiders()
    {
        // Two riders going in compatible directions (detour < max)
        await using var db = CreateInMemoryDb();
        db.PoolRegionRates.Add(new PoolRegionRate { RegionId = 1, MaxDetourKm = 5.0m, MatchTimeoutSecs = 120 });

        var ride1 = Ride.CreatePool(Guid.NewGuid(), 1, 51.50, -0.10, 51.60, -0.05);
        var ride2 = Ride.CreatePool(Guid.NewGuid(), 1, 51.51, -0.11, 51.61, -0.06);
        db.Rides.AddRange(ride1, ride2);
        await db.SaveChangesAsync();

        var redis = new Mock<IConnectionMultiplexer>();
        var redisDbs = new Mock<IDatabase>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDbs.Object);

        var entry1 = $@"{{""RideId"":""{ride1.Id}"",""Lat"":51.50,""Lng"":-0.10,""DestLat"":51.60,""DestLng"":-0.05}}";
        var entry2 = $@"{{""RideId"":""{ride2.Id}"",""Lat"":51.51,""Lng"":-0.11,""DestLat"":51.61,""DestLng"":-0.06}}";

        // Simulate queue has ride1 (oldest) and ride2
        var queueEntries = new SortedSetEntry[]
        {
            new(entry1, 1000),
            new(entry2, 1001)
        };
        redisDbs.Setup(r => r.SortedSetRangeByScoreWithScoresAsync(
            It.IsAny<RedisKey>(), It.IsAny<double>(), It.IsAny<double>(),
            It.IsAny<Exclude>(), It.IsAny<Order>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(queueEntries);

        // Lua script returns 1 (success)
        redisDbs.Setup(r => r.ScriptEvaluateAsync(
            It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(1, ResultType.Integer));

        var matcher = new PoolMatcherService(db, redis.Object, NullLogger<PoolMatcherService>.Instance);
        var matched = await matcher.TryMatchRidesAsync(1, CancellationToken.None);

        Assert.True(matched);
        var updated1 = await db.Rides.FindAsync(ride1.Id);
        var updated2 = await db.Rides.FindAsync(ride2.Id);
        Assert.Equal(RideStatus.PoolMatched, updated1!.Status);
        Assert.Equal(RideStatus.PoolMatched, updated2!.Status);
        Assert.Equal(updated1.PoolTripId, updated2.PoolTripId);
        Assert.NotEqual(updated1.PoolSlot, updated2.PoolSlot);
    }

    [Fact]
    public async Task TryMatchRidesAsync_ReturnsNoMatch_WhenDetourExceedsMax()
    {
        await using var db = CreateInMemoryDb();
        db.PoolRegionRates.Add(new PoolRegionRate { RegionId = 1, MaxDetourKm = 1.0m, MatchTimeoutSecs = 120 });

        // Two riders very far apart — detour will exceed 1.0km
        var ride1 = Ride.CreatePool(Guid.NewGuid(), 1, 0.0, 0.0, 0.01, 0.01);
        var ride2 = Ride.CreatePool(Guid.NewGuid(), 1, 50.0, 50.0, 50.01, 50.01);
        db.Rides.AddRange(ride1, ride2);
        await db.SaveChangesAsync();

        var redis = new Mock<IConnectionMultiplexer>();
        var redisDbs = new Mock<IDatabase>();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDbs.Object);

        var entry1 = $@"{{""RideId"":""{ride1.Id}"",""Lat"":0.0,""Lng"":0.0,""DestLat"":0.01,""DestLng"":0.01}}";
        var entry2 = $@"{{""RideId"":""{ride2.Id}"",""Lat"":50.0,""Lng"":50.0,""DestLat"":50.01,""DestLng"":50.01}}";

        redisDbs.Setup(r => r.SortedSetRangeByScoreWithScoresAsync(
            It.IsAny<RedisKey>(), It.IsAny<double>(), It.IsAny<double>(),
            It.IsAny<Exclude>(), It.IsAny<Order>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync([new SortedSetEntry(entry1, 1000), new SortedSetEntry(entry2, 1001)]);

        var matcher = new PoolMatcherService(db, redis.Object, NullLogger<PoolMatcherService>.Instance);
        var matched = await matcher.TryMatchRidesAsync(1, CancellationToken.None);

        Assert.False(matched);
        // Rides remain PoolQueued — not modified
        var r1 = await db.Rides.FindAsync(ride1.Id);
        Assert.Equal(RideStatus.PoolQueued, r1!.Status);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "PoolMatcherServiceTests" -v minimal
```

Expected: compile error — `PoolMatcherService` not found.

- [ ] **Step 3: Implement PoolMatcherService**

```csharp
// src/Gruuber.Rides/Application/PoolMatcherService.cs
using System.Text.Json;
using Confluent.Kafka;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Gruuber.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectionMultiplexer _redis;
    private readonly IConfiguration? _configuration;
    private readonly ILogger<PoolMatcherService> _logger;

    // Lua script: atomically removes two members from the sorted set.
    // Returns number of successfully removed members (2 = success, <2 = race lost).
    private const string RemovePairLua = @"
        local r1 = redis.call('ZREM', KEYS[1], ARGV[1])
        local r2 = redis.call('ZREM', KEYS[1], ARGV[2])
        return r1 + r2";

    // Constructor for production use (Kafka consumer + scoped DI)
    public PoolMatcherService(IServiceScopeFactory scopeFactory, IConnectionMultiplexer redis,
        IConfiguration configuration, ILogger<PoolMatcherService> logger)
    {
        _scopeFactory = scopeFactory;
        _redis = redis;
        _configuration = configuration;
        _logger = logger;
    }

    // Constructor for unit testing (direct DbContext injection)
    internal PoolMatcherService(RidesDbContext db, IConnectionMultiplexer redis, ILogger<PoolMatcherService> logger)
    {
        _redis = redis;
        _logger = logger;
        _scopeFactory = new DirectScopeFactory(db);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_configuration is null) return; // unit test mode

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
                await TryMatchRidesAsync(regionId, stoppingToken);
                consumer.Commit(result);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PoolMatcherService error processing message");
                if (result is not null) consumer.Commit(result); // move past poison pill after logging
                await Task.Delay(1000, stoppingToken);
            }
        }

        consumer.Close();
    }

    /// <summary>
    /// Attempts to match the oldest waiting ride with a compatible rider in the same region.
    /// Returns true if a match was made.
    /// Exposed as internal for unit testing.
    /// </summary>
    internal async Task<bool> TryMatchRidesAsync(int regionId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RidesDbContext>();

        var rate = await db.PoolRegionRates
            .FirstOrDefaultAsync(r => r.RegionId == regionId, ct);
        if (rate is null) return false;

        var queueKey = $"pool_queue:{regionId}";
        var redisDb = _redis.GetDatabase();

        // Load all entries in the queue (ZRANGEBYSCORE -inf +inf WITHSCORES)
        var entries = await redisDb.SortedSetRangeByScoreWithScoresAsync(queueKey);
        if (entries.Length < 2) return false;

        var oldest = entries[0];
        var oldestData = JsonSerializer.Deserialize<PoolQueueEntry>(oldest.Element.ToString())!;

        // Try to find a compatible candidate (check detour)
        for (int i = 1; i < entries.Length; i++)
        {
            var candidate = entries[i];
            var candidateData = JsonSerializer.Deserialize<PoolQueueEntry>(candidate.Element.ToString())!;

            var detourKm = CalculateDetourKm(oldestData, candidateData);
            if (detourKm > (double)rate.MaxDetourKm) continue;

            // Atomically remove both from queue (Lua prevents double-match race)
            var luaResult = await redisDb.ScriptEvaluateAsync(RemovePairLua,
                new RedisKey[] { queueKey },
                new RedisValue[] { oldest.Element, candidate.Element });

            var removed = (int)luaResult;
            if (removed < 2)
            {
                // Race lost — another instance matched one of these rides
                _logger.LogWarning("PoolMatcherService: Lua atomic remove got {Removed}/2 for region {RegionId}", removed, regionId);
                return false;
            }

            // Both removed — proceed to assign pool trip
            await AssignPoolTripAsync(db, oldestData.RideId, candidateData.RideId, regionId, ct);
            return true;
        }

        return false;
    }

    private async Task AssignPoolTripAsync(RidesDbContext db, Guid rideId1, Guid rideId2,
        int regionId, CancellationToken ct)
    {
        var ride1 = await db.Rides.FindAsync([rideId1], ct);
        var ride2 = await db.Rides.FindAsync([rideId2], ct);

        if (ride1 is null || ride2 is null)
        {
            _logger.LogError("PoolMatcherService: ride not found during pool assignment r1={R1} r2={R2}", rideId1, rideId2);
            return;
        }

        var poolTripId = Guid.NewGuid();
        var ok1 = ride1.TryAssignPool(poolTripId, slot: 1, ride1.Version);
        var ok2 = ride2.TryAssignPool(poolTripId, slot: 2, ride2.Version);

        if (!ok1 || !ok2)
        {
            _logger.LogError("PoolMatcherService: optimistic concurrency failed during pool assignment trip={TripId}", poolTripId);
            return;
        }

        var outbox1 = BuildPoolMatchedOutbox(regionId, poolTripId, rideId1, rideId2);
        var outbox2 = BuildPoolMatchedOutbox(regionId, poolTripId, rideId2, rideId1);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Set<RideOutboxEntry>().AddRange(outbox1, outbox2);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "Pool trip {TripId} matched: rides {R1} (slot 1) and {R2} (slot 2) region={RegionId}",
            poolTripId, rideId1, rideId2, regionId);
    }

    private static RideOutboxEntry BuildPoolMatchedOutbox(int regionId, Guid poolTripId, Guid thisRideId, Guid otherRideId) =>
        new()
        {
            EventType = $"ride-events-{regionId}",
            Payload = JsonSerializer.Serialize(new
            {
                EventName = "ride_pool_matched",
                PoolTripId = poolTripId,
                RideId = thisRideId,
                OtherRideId = otherRideId,
                RegionId = regionId,
                OccurredAt = DateTime.UtcNow
            })
        };

    /// <summary>
    /// Haversine distance between two riders' pickup points as a proxy for detour.
    /// Full detour calculation (route-aware) is outside scope of this PR.
    /// </summary>
    private static double CalculateDetourKm(PoolQueueEntry a, PoolQueueEntry b)
    {
        const double R = 6371.0;
        var dLat = ToRad(b.Lat - a.Lat);
        var dLng = ToRad(b.Lng - a.Lng);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(a.Lat)) * Math.Cos(ToRad(b.Lat)) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
}

internal record PoolQueueEntry(Guid RideId, Guid RiderId, double Lat, double Lng, double DestLat, double DestLng);

/// <summary>Minimal IServiceScopeFactory adapter for unit testing with a direct DbContext.</summary>
internal class DirectScopeFactory : IServiceScopeFactory
{
    private readonly RidesDbContext _db;
    public DirectScopeFactory(RidesDbContext db) => _db = db;
    public IServiceScope CreateScope() => new DirectScope(_db);

    private class DirectScope : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; }
        public DirectScope(RidesDbContext db) =>
            ServiceProvider = new DirectServiceProvider(db);
        public void Dispose() { }

        private class DirectServiceProvider : IServiceProvider
        {
            private readonly RidesDbContext _db;
            public DirectServiceProvider(RidesDbContext db) => _db = db;
            public object? GetService(Type t) => t == typeof(RidesDbContext) ? _db : null;
        }
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "PoolMatcherServiceTests" -v minimal
```

Expected: 2 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Gruuber.Rides/Application/PoolMatcherService.cs
git add tests/Gruuber.Tests/Unit/Pool/PoolMatcherServiceTests.cs
git commit -m "feat(pool): implement PoolMatcherService Kafka consumer with Lua atomic match and detour check"
```

---

## Task 7: PoolTimeoutWorker (sweep job)

**Files:**
- Create: `src/Gruuber.Rides/Application/PoolTimeoutWorker.cs`

- [ ] **Step 1: Write failing test**

```csharp
// tests/Gruuber.Tests/Unit/Pool/PoolTimeoutWorkerTests.cs
using Gruuber.Rides.Application;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class PoolTimeoutWorkerTests
{
    private static RidesDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<RidesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new RidesDbContext(opts);
    }

    [Fact]
    public async Task SweepAsync_EmitsTimeoutOutboxEvent_ForExpiredPoolQueuedRides()
    {
        await using var db = CreateInMemoryDb();
        db.PoolRegionRates.Add(new PoolRegionRate { RegionId = 1, MatchTimeoutSecs = 0 });

        // Ride created 5 seconds ago with timeout=0 → already expired
        var expiredRide = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        db.Rides.Add(expiredRide);
        await db.SaveChangesAsync();

        var worker = new PoolTimeoutWorker(db, NullLogger<PoolTimeoutWorker>.Instance);
        await worker.SweepAsync(CancellationToken.None);

        var outboxEvents = await db.Set<RideOutboxEntry>().ToListAsync();
        Assert.Single(outboxEvents);
        Assert.Contains("ride_pool_timeout", outboxEvents[0].Payload);
    }

    [Fact]
    public async Task SweepAsync_DoesNotTouch_FreshPoolQueuedRides()
    {
        await using var db = CreateInMemoryDb();
        db.PoolRegionRates.Add(new PoolRegionRate { RegionId = 1, MatchTimeoutSecs = 120 });

        var freshRide = Ride.CreatePool(Guid.NewGuid(), 1, 51.5, -0.1, 51.6, -0.05);
        db.Rides.Add(freshRide);
        await db.SaveChangesAsync();

        var worker = new PoolTimeoutWorker(db, NullLogger<PoolTimeoutWorker>.Instance);
        await worker.SweepAsync(CancellationToken.None);

        var outbox = await db.Set<RideOutboxEntry>().ToListAsync();
        Assert.Empty(outbox);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "PoolTimeoutWorkerTests" -v minimal
```

Expected: compile error.

- [ ] **Step 3: Implement PoolTimeoutWorker**

```csharp
// src/Gruuber.Rides/Application/PoolTimeoutWorker.cs
using System.Text.Json;
using Gruuber.Rides.Domain;
using Gruuber.Rides.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gruuber.Rides.Application;

public class PoolTimeoutWorker : BackgroundService
{
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly RidesDbContext? _directDb;
    private readonly ILogger<PoolTimeoutWorker> _logger;
    private readonly TimeSpan _sweepInterval = TimeSpan.FromSeconds(30);

    // Production constructor
    public PoolTimeoutWorker(IServiceScopeFactory scopeFactory, ILogger<PoolTimeoutWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // Unit test constructor
    internal PoolTimeoutWorker(RidesDbContext db, ILogger<PoolTimeoutWorker> logger)
    {
        _directDb = db;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_sweepInterval, stoppingToken);
            await SweepAsync(stoppingToken);
        }
    }

    internal async Task SweepAsync(CancellationToken ct)
    {
        RidesDbContext db;
        IServiceScope? scope = null;

        if (_directDb is not null)
        {
            db = _directDb;
        }
        else
        {
            scope = _scopeFactory!.CreateScope();
            db = scope.ServiceProvider.GetRequiredService<RidesDbContext>();
        }

        try
        {
            var rates = await db.PoolRegionRates.ToListAsync(ct);

            foreach (var rate in rates)
            {
                var cutoff = DateTime.UtcNow.AddSeconds(-rate.MatchTimeoutSecs);

                var expiredRides = await db.Rides
                    .Where(r => r.Status == RideStatus.PoolQueued
                                && r.RegionId == rate.RegionId
                                && r.CreatedAt <= cutoff)
                    .ToListAsync(ct);

                if (expiredRides.Count == 0) continue;

                var outboxEntries = expiredRides.Select(r => new RideOutboxEntry
                {
                    EventType = $"ride-events-{rate.RegionId}",
                    Payload = JsonSerializer.Serialize(new
                    {
                        EventName = "ride_pool_timeout",
                        RideId = r.Id,
                        RiderId = r.RiderId,
                        RegionId = rate.RegionId,
                        Reason = "no_match",
                        NotifyUser = true,
                        OccurredAt = DateTime.UtcNow
                    })
                }).ToList();

                await using var tx = await db.Database.BeginTransactionAsync(ct);
                db.Set<RideOutboxEntry>().AddRange(outboxEntries);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                _logger.LogWarning(
                    "PoolTimeoutWorker: {Count} pool rides timed out in region {RegionId}",
                    expiredRides.Count, rate.RegionId);
            }
        }
        finally
        {
            scope?.Dispose();
        }
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "PoolTimeoutWorkerTests" -v minimal
```

Expected: 2 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Gruuber.Rides/Application/PoolTimeoutWorker.cs
git add tests/Gruuber.Tests/Unit/Pool/PoolTimeoutWorkerTests.cs
git commit -m "feat(pool): add PoolTimeoutWorker sweep job to emit timeout events for expired pool rides"
```

---

## Task 8: API endpoint and module registration

**Files:**
- Modify: `src/Gruuber.Api/Controllers/RidesController.cs`
- Modify: `src/Gruuber.Rides/RidesModule.cs`
- Modify: `src/Gruuber.Api/Program.cs`

- [ ] **Step 1: Add accept-solo-upgrade endpoint to RidesController**

In `src/Gruuber.Api/Controllers/RidesController.cs`, add the handler injection and endpoint:

```csharp
// Add to constructor parameters:
private readonly AcceptSoloUpgradeHandler _soloUpgradeHandler;

// In constructor body:
_soloUpgradeHandler = soloUpgradeHandler;

// Add endpoint method:
[HttpPost("{id:guid}/accept-solo-upgrade")]
[Authorize(Policy = "rider")]
public async Task<IActionResult> AcceptSoloUpgrade(Guid id, [FromBody] AcceptSoloUpgradeRequest request, CancellationToken cancellationToken)
{
    var cmd = new AcceptSoloUpgradeCommand(id, request.ExpectedVersion, _currentUser.UserId, _currentUser.RegionId);
    var result = await _soloUpgradeHandler.HandleAsync(cmd, cancellationToken);
    return result.ToHttpResult(this);
}
```

Add at bottom of file:
```csharp
public record AcceptSoloUpgradeRequest([Range(1, long.MaxValue)] long ExpectedVersion);
```

- [ ] **Step 2: Update RidesModule to register new services**

In `src/Gruuber.Rides/RidesModule.cs`, add:

```csharp
services.AddScoped<AcceptSoloUpgradeHandler>();
services.AddHostedService<PoolMatcherService>();
services.AddHostedService<PoolTimeoutWorker>();
```

- [ ] **Step 3: Build and run all unit tests**

```bash
dotnet build Gruuber.slnx -c Release --no-incremental 2>&1 | tail -5
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "Category!=Integration" -v minimal
```

Expected: Build succeeds; all unit tests PASS.

- [ ] **Step 4: Commit**

```bash
git add src/Gruuber.Api/Controllers/RidesController.cs src/Gruuber.Rides/RidesModule.cs
git commit -m "feat(pool): register PoolMatcherService, PoolTimeoutWorker; add accept-solo-upgrade endpoint"
```

---

## Task 9: Integration test stubs

**Files:**
- Create: `tests/Gruuber.Tests/Integration/Pool/RidePoolingIntegrationTests.cs`

- [ ] **Step 1: Create integration test stubs**

```csharp
// tests/Gruuber.Tests/Integration/Pool/RidePoolingIntegrationTests.cs
using Xunit;

/// <summary>
/// Integration tests for ride pooling end-to-end flows.
/// Requires Docker (Postgres + Redis + Kafka via Testcontainers).
/// Run with: dotnet test --filter "Category=Integration"
/// </summary>
public class RidePoolingIntegrationTests
{
    [Fact(Skip = "Requires Docker")]
    [Trait("Category", "Integration")]
    public async Task HappyPath_TwoRidersMatch_BothCompleteTrip()
    {
        // Arrange: seed PoolRegionRate for region 1
        // Act: POST /v1/rides/request (pool) for 2 riders with compatible routes
        // Wait for PoolMatcherService to consume ride_pool_queued
        // Assert: both rides transition to PoolMatched, then Matched
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires Docker")]
    [Trait("Category", "Integration")]
    public async Task TimeoutFlow_NoMatchFound_SignalRNudgeSent_RiderAcceptsSolo()
    {
        // Arrange: seed PoolRegionRate with MatchTimeoutSecs=1 (very short)
        // Act: POST pool ride; wait for PoolTimeoutWorker to emit timeout event
        // Act: POST /v1/rides/{id}/accept-solo-upgrade
        // Assert: ride transitions to Requested (solo)
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires Docker")]
    [Trait("Category", "Integration")]
    public async Task RaceCondition_TwoMatcherInstances_OnlyOneSucceeds()
    {
        // Arrange: 2 PoolMatcherService instances, same queue entry
        // Assert: exactly one match formed; no double-match
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires Docker")]
    [Trait("Category", "Integration")]
    public async Task PrivacyGuard_GetRide_NeverReturnsOtherRidersCoords()
    {
        // Arrange: matched pool trip (2 riders)
        // Act: GET /v1/rides/{rider1RideId} as rider 1
        // Assert: response does NOT contain rider 2's pickup/dropoff coordinates
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
git add tests/Gruuber.Tests/Integration/Pool/
git commit -m "test(pool): add integration test stubs for ride pooling flows and privacy invariant"
```

---

## Completion Checklist

- [ ] `/v1/rides/{id}/accept-solo-upgrade` endpoint exists under correct versioned path
- [ ] `ride_views` not written to directly — Kafka consumer only updates it
- [ ] All pool Kafka events go through `IOutboxPublisher` (outbox table)
- [ ] `version` check on all `rides` updates (`TryAssignPool`, `TryUpgradeToSolo`)
- [ ] `PoolTripId`, `PoolSlot`, `RegionId` in all pool log entries
- [ ] Lua atomic remove prevents double-match race condition
- [ ] Kafka DLQ fallback: `PoolMatcherService` commits past poison pills after logging error
- [ ] `CancellationToken` propagated in all async methods
- [ ] Rider's own `GET /v1/rides/{id}` never returns other rider's coords (enforced by not storing them on the same ride row)
