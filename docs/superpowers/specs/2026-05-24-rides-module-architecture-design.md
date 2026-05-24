# Ride Module Architecture Design

**Date:** 2026-05-24  
**Status:** Draft  
**Module:** Gruuber.Rides  
**Approach:** Keep the existing ride aggregate as the source of truth, and move orchestration into small, single-purpose application services.

---

## Overview

The current ride module already has clear functional slices for ride requests, driver matching, pool matching, timeout handling, and ride state transitions. The problem is not missing functionality; it is that orchestration, state mutation, and event payload construction are spread across multiple handlers and background services. That creates duplication, makes the control flow harder to follow, and increases the chance that future ride variants will drift from the solo flow.

This design keeps the existing behavior and boundaries of the ride module, but tightens them around a small set of responsibilities:

- `Ride` remains the aggregate root and owns state transitions plus optimistic concurrency checks.
- Application services coordinate use cases such as ride requests, driver matching, pool matching, and timeout sweeps.
- A small outbox factory centralizes event payload creation so the same event shapes are not rebuilt in multiple places.
- Infrastructure components such as Redis and Kafka remain outside the domain model.

The goal is to make the ride flow easier to extend without turning the module into a framework.

---

## Goals

- Reduce duplication in ride request, match, and pool workflows.
- Keep ride state changes and version checks inside the aggregate.
- Centralize outbox payload creation and event naming.
- Preserve existing API contracts and the `ride_views` read model behavior.
- Leave room for future ride variants without hard-coding more special cases into handlers.

## Non-Goals

- Rewriting the ride module into a full CQRS framework.
- Changing endpoint versions or request/response shapes beyond internal refactoring.
- Moving Kafka or Redis responsibilities into the domain layer.
- Refactoring unrelated modules such as Orders, Payments, Chat, or Analytics.

---

## Design Principles

This design follows a narrow set of rules:

- **Single Responsibility:** each service owns one use case or one reusable concern.
- **Open/Closed:** new ride behaviors should be added by extending orchestration services, not by copying whole handlers.
- **Dependency Inversion:** orchestration depends on abstractions and the aggregate, not the other way around.
- **DRY:** event payloads, queue entry shapes, and transaction patterns should be shared once.
- **KISS:** keep the number of new types small and avoid introducing strategy hierarchies unless the module genuinely needs them.
- **YAGNI:** do not create generic “workflow engines” or broad matching frameworks that are not required by the current flows.

---

## Proposed Architecture

### Core boundaries

- `Ride` is the only place that mutates ride state.
- `RideRequestCoordinator` owns the request flow and decides whether a request is solo or pool.
- `DriverMatchCoordinator` owns solo matching and winning-driver application.
- `PoolMatchCoordinator` owns pool pairing and pool-trip assignment.
- `PoolTimeoutCoordinator` owns timeout sweeps and timeout-driven ride updates.
- `RideOutboxFactory` owns event payload creation and event type naming.

### Existing components to keep

- `RequestRideHandler` remains the API entry point for ride creation, but becomes a thin wrapper.
- `MatchDriverHandler` remains the API entry point for solo driver matching, but delegates all real work.
- `PoolMatcherService` remains the Kafka-triggered background worker, but delegates the matching decision.
- `PoolTimeoutWorker` remains the scheduled background worker, but delegates timeout processing.
- `GetRideStatusHandler` continues to serve the query path with its existing fallback to the write model when the read model is not yet populated.

### Internal dependency order

1. Handlers and background workers accept input and logging context.
2. Coordinators validate use-case preconditions and orchestrate a unit of work.
3. Coordinators call `Ride` to mutate state.
4. Coordinators use `RideOutboxFactory` to create outbox entries.
5. Repositories and EF Core persist the result inside a transaction.

This keeps domain logic inside the aggregate and application logic in the application layer.

---

## Components

### `RideRequestCoordinator`

Owns the request flow for both solo and pool rides.

Responsibilities:

- Resolve surge pricing.
- Create the ride aggregate.
- Build the initial outbox event.
- For pool requests, create the Redis queue entry after the database transaction commits.
- Return a response object that matches the current API contract.

What it depends on:

- `RidesDbContext` or a repository abstraction over it.
- `ISurgePricingService`.
- Redis for pool queue writes.
- `RideOutboxFactory`.

### `DriverMatchCoordinator`

Owns solo driver assignment.

Responsibilities:

- Load the ride.
- Fetch scored candidates from `IDriverScoringService`.
- Choose the best driver.
- Apply the version-checked match on the ride aggregate.
- Emit a single `driver_matched` outbox entry.

What it depends on:

- `RidesDbContext` or a repository abstraction.
- `IDriverScoringService`.
- `RideOutboxFactory`.

### `PoolMatchCoordinator`

Owns the pool pairing workflow.

Responsibilities:

- Read compatible queue entries from Redis.
- Apply the detour rule for candidate selection.
- Atomically remove matched riders from the queue.
- Assign a shared pool trip and pool slots on the ride aggregate.
- Emit either match or compensation outbox entries.

What it depends on:

- `RidesDbContext` or a repository abstraction.
- Redis for queue reads and atomic removals.
- `RideOutboxFactory`.

### `PoolTimeoutCoordinator`

Owns timeout sweeps for queued pool rides.

Responsibilities:

- Find expired pool rides by region.
- Transition expired rides through the aggregate.
- Emit timeout outbox events.

What it depends on:

- `RidesDbContext` or a repository abstraction.
- `RideOutboxFactory`.

### `RideOutboxFactory`

Owns the construction of ride event payloads.

Responsibilities:

- Create outbox entries with consistent event names.
- Standardize common metadata such as `RegionId`, `RideId`, and `OccurredAt`.
- Keep JSON shape changes in one place.

What it depends on:

- Nothing beyond the data passed into it.

This is intentionally small. It is a shared builder, not a general-purpose messaging abstraction.

---

## Data Flow

### Solo ride request

1. API controller calls `RequestRideHandler`.
2. Handler delegates to `RideRequestCoordinator`.
3. Coordinator resolves surge pricing and creates the `Ride` aggregate.
4. Coordinator creates a `ride_requested` outbox entry through `RideOutboxFactory`.
5. Coordinator persists the ride and outbox entry in one transaction.
6. Response returns `202 Accepted` with the current request payload contract.

### Solo driver matching

1. API controller calls `MatchDriverHandler`.
2. Handler delegates to `DriverMatchCoordinator`.
3. Coordinator loads the ride and scored candidates.
4. Coordinator selects the best candidate and calls `ride.TryMatch(...)`.
5. Coordinator persists the updated ride and a `driver_matched` outbox entry.
6. If no candidates are available, the coordinator returns `pending_match` without mutating the ride.

### Pool ride request

1. API controller calls `RequestRideHandler`.
2. Handler delegates to `RideRequestCoordinator`.
3. Coordinator validates pool prerequisites and creates the `Ride` aggregate in `PoolQueued` status.
4. Coordinator persists the ride and `ride_pool_queued` outbox entry.
5. After commit, coordinator enqueues the Redis pool record.
6. Response returns `202 Accepted` with `pool_queued` and timeout metadata.

### Pool match sweep

1. Kafka consumer in `PoolMatcherService` receives a `ride_pool_queued` event.
2. Service delegates to `PoolMatchCoordinator`.
3. Coordinator reads the region queue and checks detour compatibility.
4. If a valid pair exists, it atomically removes both queue entries.
5. Coordinator loads both rides and calls `ride.TryAssignPool(...)` on each.
6. Coordinator persists both rides plus the relevant outbox entries.
7. If a race or missing ride is detected, coordinator emits a compensation event and stops.

### Pool timeout sweep

1. Scheduled worker `PoolTimeoutWorker` triggers on its interval.
2. Worker delegates to `PoolTimeoutCoordinator`.
3. Coordinator finds expired rides per region.
4. Coordinator applies the timeout transition on each ride.
5. Coordinator persists the ride updates and timeout outbox entries.
6. Any rider-facing follow-up remains in the notification layer, not the ride aggregate.

---

## Error Handling

### Expected failures

- **Ride not found:** return `404` with the existing error code shape.
- **Invalid transition:** return `400` and do not persist a partial mutation.
- **Optimistic concurrency conflict:** return `409 Conflict` with `RESOURCE_CONFLICTED` and the current version only.
- **No matching driver or no pool candidate:** return `202 Accepted` with pending status when the workflow is intentionally asynchronous.
- **Missing pool configuration:** return `400` for pool requests in unsupported regions.

### Infrastructure failures

- **Redis unavailable for pool queue writes:** the request should fail clearly rather than silently creating a ride that cannot be matched.
- **Redis race during pool pairing:** treat it as a benign retry condition, not a fatal error.
- **Kafka consumer poison message:** log the failure, advance the offset if the consumer policy requires it, and rely on the existing DLQ path.

### Transaction rules

- Ride state changes and outbox inserts must stay in the same database transaction.
- Redis queue writes for pool requests happen after the database transaction commits.
- A failed outbox write should fail the use case rather than partially committing the ride.

---

## Testing Strategy

### Unit tests

- `Ride` state transitions: valid transitions succeed, invalid transitions fail, version mismatches are rejected.
- `RideRequestCoordinator`: solo and pool request paths emit the correct outbox entries and return the correct result shape.
- `DriverMatchCoordinator`: best candidate selection, no-candidate path, and concurrency conflict path.
- `PoolMatchCoordinator`: detour filtering, atomic queue removal failure, missing ride compensation, and successful pool assignment.
- `PoolTimeoutCoordinator`: expired ride detection and timeout event emission.
- `RideOutboxFactory`: event payload shapes remain stable.

### Integration tests

- `POST /v1/rides/request` creates solo and pool rides with the expected response shape.
- `POST /v1/rides/{id}/accept-solo-upgrade` returns the current state and preserves optimistic concurrency behavior.
- Pool matching end-to-end path verifies Redis queue interaction and outbox writes.
- Timeout sweep end-to-end path verifies that expired rides are transitioned and outbox entries are created.

### Regression focus

The most important regression risk is that refactoring orchestration could accidentally move business rules out of `Ride` or duplicate them in coordinators. Tests should therefore assert both the externally visible result and the internal version-checked state change.

---

## Implementation Notes

- Keep the current API versioning and endpoints unchanged.
- Do not introduce a broad strategy hierarchy for ride types yet.
- Prefer small extraction steps over a large rewrite.
- Keep background workers thin; they should schedule or trigger work, not contain workflow logic.
- Preserve the current `ride_views` behavior and do not write to it directly from command paths.

---

## Review Checklist

- No duplicated event payload construction remains in handlers or workers.
- Ride state mutation remains inside the `Ride` aggregate.
- Coordinators do not depend on each other cyclically.
- Pool and solo flows share reusable outbox and transaction behavior.
- The design does not add generic abstractions that are not needed for the current module.
