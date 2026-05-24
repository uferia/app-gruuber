# Ride Module Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce duplication in the ride module by moving request, matching, timeout, and outbox orchestration into small focused services while preserving existing endpoints and behavior.

**Architecture:** Keep `Ride` as the source of truth for state transitions and optimistic concurrency. Add small coordinators for request, driver match, pool match, and timeout workflows, plus a dedicated outbox factory for consistent event payloads. Keep handlers and background workers as thin entry points.

**Tech Stack:** ASP.NET Core 8, EF Core, PostgreSQL, Redis, Kafka, xUnit, Moq, InMemory provider.

---

### Task 1: Add a shared ride outbox factory

**Files:**
- Create: `src/Gruuber.Rides/Application/RideOutboxFactory.cs`
- Modify: `src/Gruuber.Rides/Application/Commands/RequestRideHandler.cs:1-180`
- Modify: `src/Gruuber.Rides/Application/Commands/MatchDriverHandler.cs:1-120`
- Modify: `src/Gruuber.Rides/Application/Commands/TransitionRideHandler.cs:1-120`
- Modify: `src/Gruuber.Rides/Application/Commands/AcceptSoloUpgradeHandler.cs:1-120`
- Modify: `src/Gruuber.Rides/Application/PoolMatcherService.cs:1-320`
- Modify: `src/Gruuber.Rides/Application/PoolTimeoutWorker.cs:1-220`
- Test: `tests/Gruuber.Tests/Unit/Pool/AcceptSoloUpgradeHandlerTests.cs`
- Test: `tests/Gruuber.Tests/Unit/Pool/PoolMatcherServiceTests.cs`
- Test: `tests/Gruuber.Tests/Unit/Pool/PoolTimeoutWorkerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
var factory = new RideOutboxFactory();
var entry = factory.CreateRidePoolQueued(1, Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.Parse("00000000-0000-0000-0000-000000000002"), 51.5, -0.1, 51.6, -0.05);
Assert.Equal("ride-events-1", entry.EventType);
Assert.Contains("ride_pool_queued", entry.Payload);
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter RideOutboxFactory`
Expected: fail because `RideOutboxFactory` does not exist yet.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace Gruuber.Rides.Application;

public sealed class RideOutboxFactory
{
    public RideOutboxEntry CreateRideRequested(...)
    public RideOutboxEntry CreateRidePoolQueued(...)
    public RideOutboxEntry CreateDriverMatched(...)
    public RideOutboxEntry CreateRideStatusChanged(...)
    public RideOutboxEntry CreateRidePoolUpgraded(...)
    public RideOutboxEntry CreateRidePoolTimeout(...)
    public RideOutboxEntry CreateRidePoolMatched(...)
    public RideOutboxEntry CreateRidePoolMatchFailed(...)
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter RideOutboxFactory`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Gruuber.Rides/Application/RideOutboxFactory.cs tests/Gruuber.Tests/Unit/Pool/*.cs src/Gruuber.Rides/Application/Commands/*.cs src/Gruuber.Rides/Application/Pool*.cs
git commit -m "refactor(rides): centralize ride outbox payloads"
```

### Task 2: Extract ride request coordination

**Files:**
- Create: `src/Gruuber.Rides/Application/RideRequestCoordinator.cs`
- Modify: `src/Gruuber.Rides/Application/Commands/RequestRideHandler.cs:1-220`
- Test: `tests/Gruuber.Tests/Unit/Pool/RequestPoolRideHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
var handler = new RequestRideHandler(db, surge.Object, redis.Object, NullLogger<RequestRideHandler>.Instance);
var result = await handler.HandleAsync(new RequestRideCommand(Guid.NewGuid(), "pool", 51.5, -0.1, 1, 51.6, -0.05));
Assert.True(result.IsSuccess);
Assert.Equal("PoolQueued", result.Data!.Status);
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter RequestPoolRideHandlerTests`
Expected: fail until the coordinator is wired up.

- [ ] **Step 3: Write minimal implementation**

```csharp
internal sealed class RideRequestCoordinator
{
    public Task<ApplicationResult<RequestRideResponse>> HandleAsync(RequestRideCommand command, CancellationToken cancellationToken = default)
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter RequestPoolRideHandlerTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Gruuber.Rides/Application/RideRequestCoordinator.cs src/Gruuber.Rides/Application/Commands/RequestRideHandler.cs tests/Gruuber.Tests/Unit/Pool/RequestPoolRideHandlerTests.cs
git commit -m "refactor(rides): extract ride request coordination"
```

### Task 3: Extract driver matching coordination

**Files:**
- Create: `src/Gruuber.Rides/Application/DriverMatchCoordinator.cs`
- Modify: `src/Gruuber.Rides/Application/Commands/MatchDriverHandler.cs:1-220`
- Test: `tests/Gruuber.Tests/Unit/Pool/MatchDriverHandlerTests.cs` (create if absent)

- [ ] **Step 1: Write the failing test**

```csharp
var handler = new MatchDriverHandler(db, scoring.Object, NullLogger<MatchDriverHandler>.Instance);
var result = await handler.HandleAsync(new MatchDriverCommand(ride.Id, 1, 1));
Assert.True(result.IsSuccess);
Assert.Equal(202, result.StatusCode);
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter MatchDriverHandlerTests`
Expected: fail until the new test exists and the refactor is wired.

- [ ] **Step 3: Write minimal implementation**

```csharp
internal sealed class DriverMatchCoordinator
{
    public Task<ApplicationResult<MatchDriverResponse>> HandleAsync(MatchDriverCommand command, CancellationToken cancellationToken = default)
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter MatchDriverHandlerTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Gruuber.Rides/Application/DriverMatchCoordinator.cs src/Gruuber.Rides/Application/Commands/MatchDriverHandler.cs tests/Gruuber.Tests/Unit/Pool/MatchDriverHandlerTests.cs
git commit -m "refactor(rides): extract driver match coordination"
```

### Task 4: Extract pool matching and timeout coordination

**Files:**
- Create: `src/Gruuber.Rides/Application/PoolMatchCoordinator.cs`
- Create: `src/Gruuber.Rides/Application/PoolTimeoutCoordinator.cs`
- Modify: `src/Gruuber.Rides/Application/PoolMatcherService.cs:1-320`
- Modify: `src/Gruuber.Rides/Application/PoolTimeoutWorker.cs:1-220`
- Test: `tests/Gruuber.Tests/Unit/Pool/PoolMatcherServiceTests.cs`
- Test: `tests/Gruuber.Tests/Unit/Pool/PoolTimeoutWorkerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
var matcher = new PoolMatcherService(db, redis.Object, NullLogger<PoolMatcherService>.Instance);
var matched = await matcher.TryMatchRidesAsync(1, CancellationToken.None);
Assert.True(matched);
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter PoolMatcherServiceTests`
Expected: fail until coordinator is wired in.

- [ ] **Step 3: Write minimal implementation**

```csharp
internal sealed class PoolMatchCoordinator
{
    public Task<bool> TryMatchRidesAsync(int regionId, CancellationToken ct)
}

internal sealed class PoolTimeoutCoordinator
{
    public Task SweepAsync(CancellationToken ct)
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "PoolMatcherServiceTests|PoolTimeoutWorkerTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Gruuber.Rides/Application/PoolMatchCoordinator.cs src/Gruuber.Rides/Application/PoolTimeoutCoordinator.cs src/Gruuber.Rides/Application/PoolMatcherService.cs src/Gruuber.Rides/Application/PoolTimeoutWorker.cs tests/Gruuber.Tests/Unit/Pool/PoolMatcherServiceTests.cs tests/Gruuber.Tests/Unit/Pool/PoolTimeoutWorkerTests.cs
git commit -m "refactor(rides): extract pool workflow coordination"
```

### Task 5: Validate the refactor end to end

**Files:**
- Modify: `src/Gruuber.Rides/RidesModule.cs`
- Test: `tests/Gruuber.Tests/Unit/Pool/*.cs`
- Test: `tests/Gruuber.Tests/Integration/Pool/RidePoolingIntegrationTests.cs`

- [ ] **Step 1: Run the focused ride tests**

Run: `dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter Pool`
Expected: all pool unit tests pass.

- [ ] **Step 2: Run the ride module build/test slice**

Run: `dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter Ride --no-restore`
Expected: pass or report only pre-existing unrelated failures.

- [ ] **Step 3: Verify DI and startup compile**

Run: `dotnet build src/Gruuber.Rides/Gruuber.Rides.csproj --no-restore`
Expected: build succeeds with the new services and handler wiring.

- [ ] **Step 4: Commit**

```bash
git add src/Gruuber.Rides/RidesModule.cs tests/Gruuber.Tests/Integration/Pool/RidePoolingIntegrationTests.cs
git commit -m "refactor(rides): finish module orchestration cleanup"
```
