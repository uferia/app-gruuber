# Copilot Instructions

## Project Overview

This is **Gruuber** — a combined ride-hailing + food delivery platform built as a **modular monolith (ASP.NET Core 8)** designed to evolve into microservices. The architecture is CQRS-first with Kafka-driven event flows, Redis for real-time geo and caching, and SignalR for live tracking.

**Modules:** Ride, Order, Payment, Tracking, Auth  
**Full spec:** [`spec_1_uber_food_modular_monolith.md`](../spec_1_uber_food_modular_monolith.md)

---

## Architecture

### CQRS + Outbox (Critical)

- Controllers route to **Command Handlers** (writes) or **Query Handlers** (reads) via MediatR.
- Writes persist to PostgreSQL and write to an `outbox` table — **never publish Kafka events directly from the application layer**.
- Use `IOutboxPublisher` for all event publishing.
- Read models (`ride_views`) are populated **asynchronously** by Kafka consumers. Never query `ride_views` for transactional writes, and never store PII there.

### Optimistic Concurrency (All critical entities)

All writes to `rides`, `orders`, and `payments` must use a `version` column check:

```sql
UPDATE rides
SET status = 'matched', version = version + 1
WHERE id = :rideId AND version = :expectedVersion;
```

If `rowsAffected == 0`, return `409 Conflict` with error code `RESOURCE_CONFLICTED` and minimal metadata (`entity_id`, `current_version`) — require client to `GET` fresh state before retrying.

### Request/Response Pattern

Use `ApplicationResult<T>` or `Result<T>` for all API responses. Return `202 Accepted` for async operations (ride creation, payment initiation). Never return full entity state on conflict responses.

### API Versioning

All endpoints use URL-based versioning: `/v1/rides/request`, `/v1/orders/create`. Non-breaking additions stay in `v1`; breaking changes introduce `/v2`. Never modify existing versioned endpoints.

### Ride Request Flow

1. Check Redis GEO (`driver_locations:{regionId}`) for nearby drivers (3–5km radius).
2. Score candidates: `w1*(1/(1+distKm)) + w2*rating + w3*availability` — pick highest.
3. Create `Ride` in DB; write event to outbox.
4. `ride_views` updated by Kafka consumer — not inline.
5. Return `202 Accepted`.

**Zero candidates:** Ride stays `requested`; return `202` with `pending_match`. Retry matching with expanding radius/backoff. Notify client via SignalR on assignment, or client polls `GET /v1/rides/{id}` as fallback.

### Payment Flow

1. `POST /v1/payments/initiate` validates, persists payment record, and writes to outbox — return `202 Accepted`.
2. Background worker publishes `payment_initiated` to Kafka.
3. On external provider callback: publish `payment_success` or `payment_failed` via Kafka.
4. **Timeout handling:** If no callback within 30s, poll provider/webhook. After 15 minutes with no resolution, set status to `failed_timeout` and emit compensating event `payment_timeout` with fields: `entity_id`, `reason`, `refund_required`, `notify_user`.

### Order Saga Compensation

Order lifecycle saga: `Create → Assign → Complete/Fail`. Each transition must have a defined compensation event (e.g., payment refund on failure). Compensation events must be defined in the Kafka contract before implementation.

### State Machines

- **Ride:** `requested → matched → en_route → arrived → completed → cancelled`
- **Order:** `placed → accepted → preparing → ready → picked_up → delivered`

---

## Key Conventions

### Kafka & Resilience

- Consumers: `IExponentialBackoff` with jitter, max **5 retries**, then route to DLQ topic (`ride-events-dlq-{region}`).
- Topics are region-scoped: `ride-events-{region}`, `order-events-{region}`, `payment-events-{region}`.
- Do not swallow `System.Exception` in Kafka consumers — trigger DLQ logic.

### Redis

- Driver locations stored as GEO with **10s TTL**: key `driver_locations:{regionId}`.
- On TTL expiry + 5s grace, atomically mark driver `offline` and remove from GEO.
- Rate limiting uses Redis-backed token bucket (Lua script for atomicity). Keys: `rate_limit:{role}:{userId}`.
  - Rider: 100 req/min | Driver: 300 req/min + 20 req/s (location updates) | Restaurant: 200 req/min
- Redis also serves as the **SignalR backplane** for multi-instance fan-out.

### Locking Rules

- **Optimistic concurrency** (version column) for all web endpoint writes.
- `SELECT FOR UPDATE` row locks are **only** for short-duration background jobs — never in web request handlers.

### SignalR

- Group clients by `rideId` for location updates.
- Throttle pushes to state changes only — not heartbeats.

### Auth

- JWT in `Authorization: Bearer` header; claims: `sub`, `role`, `region_id`, `exp`.
- Validate scopes (e.g., `rides.read`) on claims.
- Access token TTL: 15 min. Refresh token TTL: 30 days (stored hashed, rotated on use).
- YARP handles JWT validation: JWKS cached for 1 hour ± 10% jitter; refresh on `kid` miss or key rotation. Routes requests by `region_id` claim.

### Logging & Observability

- Structured JSON logs via **Serilog**. Always include: `TraceId`, `SpanId`, `RegionId`, `RideId`, `OrderId`.
- Log levels: `Error` for failures, `Warning` for retries, `Info` for lifecycle events.
- **OpenTelemetry** traces propagated across Kafka headers, DB, and SignalR connections.
- Health endpoints: `GET /health` (liveness), `GET /health/readiness` (requires Redis + Kafka active).

### Transactions & EF Core

- Wrap critical operations: `await using var tx = await context.Database.BeginTransactionAsync()`.
- Do **not** bypass EF Core with raw SQL except for schema migrations.
- Always propagate `CancellationToken` in async methods, especially in background jobs.

### DB Schema Reference

```sql
-- Write tables (all include region_id and version for concurrency)
rides(id UUID, rider_id, driver_id, status, region_id INT, version BIGINT DEFAULT 1, created_at)
orders(id UUID, rider_id, restaurant_id, driver_id, status, total_amount, region_id INT, version BIGINT DEFAULT 1, created_at)
order_items(id UUID, order_id, item_name, quantity, unit_price, subtotal)
payments(id UUID, entity_id, type, status, amount, region_id)
restaurants(id UUID, name, status, region_id, created_at)

-- Event persistence
outbox(id UUID, payload JSONB, status TEXT)

-- Read model (denormalized, Kafka-populated only)
ride_views(ride_id UUID, driver_name, status, lat DOUBLE PRECISION, lng DOUBLE PRECISION)
```

Schema changes are managed via EF Core migrations or Flyway — forward-only, idempotent in CI/CD.

### Maintenance Rules

- Completed rides archived to an archive table after **90 days**.
- Nightly job prunes stale `ride_views` rows (TTL > 10s).
- Key metrics to monitor: `KafkaConsumerFailures`, `PaymentTimeouts`.

### Performance

- Index `RideId`, `DriverId`, `Status` on `ride_views`.
- Use Redis pipeline calls for bulk GEO lookups.
- Batch Kafka writes for Order/OrderItem events (target ≤5ms).

---

## Latency SLAs

For synchronous app-owned request handling (excludes external provider callbacks):

| Percentile | Target |
|---|---|
| p50 | < 150ms |
| p95 | < 400ms |
| p99 | < 800ms |
| Timeout | 2s |

---

## Pre-Implementation Checklist

When implementing any feature, verify:

1. **Versioning** — Does the endpoint start with `/v1/`?
2. **Read vs. Write** — Is `ride_views` involved? Never write to it directly.
3. **Outbox** — All Kafka events go through `IOutboxPublisher`, not direct publish.
4. **Concurrency** — Is `version` check logic in place for `rides`/`orders`/`payments`?
5. **Observability** — Are `TraceId` and domain IDs (`RideId`, `OrderId`, `RegionId`) in logs?
6. **Redis TTLs** — Are GEO heartbeat TTLs and stale-driver cleanup considered?
7. **Resilience** — Is a Kafka DLQ fallback defined? Is circuit breaker in place for external calls?
8. **CancellationToken** — Passed through all async methods?

---

## Testing

- Unit tests: mock Kafka/Redis with **Moq**.
- Integration tests: use **Testcontainers** to spin up Postgres, Redis, and Kafka containers.
- Config: use `appsettings.json` or `UserSecrets` per environment (Dev/Stage/Prod).
