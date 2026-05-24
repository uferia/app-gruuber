# Ride Pooling Design

**Date:** 2026-05-23  
**Status:** Approved  
**Module:** Gruuber.Rides  
**Approach:** Ride Type Variant (extends existing `rides` table)

---

## Overview

Ride Pooling allows up to 2 riders travelling in compatible directions to share a single driver, reducing individual fares. Riders are matched in real-time against other waiting pool requests within the same region. Pool rides are a strict ride type — they do not mix with solo rides, and drivers must be pool-capable.

Fare discounts and matching parameters are configured per region by admins.

---

## Constraints & Decisions

| Decision | Choice |
|---|---|
| Max riders per pool trip | 2 |
| Matching strategy | Live real-time matching (not time-window batching) |
| Solo/pool mixing | Pool requests only match with other pool requests |
| Rider privacy | Each rider sees only their own pickup/dropoff — never the other's |
| Fare model | Admin-configured discount % per region (`pool_region_rates`) |
| Timeout fallback | Cancel pool → SignalR nudge → rider chooses solo upgrade or full cancel |

---

## Data Model

### Schema Changes to `rides`

```sql
ALTER TABLE rides ADD COLUMN ride_type TEXT NOT NULL DEFAULT 'solo';
-- 'solo' | 'pool'

ALTER TABLE rides ADD COLUMN pool_trip_id UUID NULL;
-- groups the 2 rides belonging to the same pool trip

ALTER TABLE rides ADD COLUMN pool_slot INT NULL;
-- 1 = first rider (defines primary route), 2 = second rider (added stop)
```

### New Table: `pool_region_rates`

```sql
CREATE TABLE pool_region_rates (
  region_id         INT PRIMARY KEY,
  discount_pct      NUMERIC NOT NULL,       -- e.g. 0.20 = 20% off solo fare
  match_timeout_secs INT NOT NULL DEFAULT 120,
  max_detour_km     NUMERIC NOT NULL DEFAULT 2.0,
  updated_at        TIMESTAMP
);
```

### Read Model (`ride_views`) additions

```sql
ALTER TABLE ride_views ADD COLUMN ride_type TEXT;
ALTER TABLE ride_views ADD COLUMN pool_slot INT;
-- No other rider's data is ever stored here (privacy constraint)
```

### Redis Pool Queue

```
Key:   pool_queue:{regionId}   (Sorted Set, score = request timestamp)
Value: { rideId, riderId, lat, lng, destLat, destLng, requestedAt }
TTL:   match_timeout_secs (from pool_region_rates)
```

---

## State Machine

### Solo rides (unchanged)

```
requested → matched → en_route → arrived → completed → cancelled
```

### Pool rides (new states)

```
pool_queued → pool_matched → matched → en_route → partial_dropoff → arrived → completed → cancelled
```

- **`pool_queued`** — ride created, rider waiting in Redis queue for a match
- **`pool_matched`** — two riders paired; driver matching score now runs
- **`matched`** — driver assigned (same as solo from this point)
- **`partial_dropoff`** — Rider 1 dropped off; driver en route to Rider 2's destination
- All other states behave identically to solo rides

---

## Request Flow

### `POST /v1/rides/request` — pool ride

1. Validate `pool_region_rates` exists for rider's `region_id` (else `400 Bad Request`)
2. Create `rides` row: `status=pool_queued`, `ride_type=pool`, `pool_slot=NULL`
3. Write `ride_pool_queued` event to outbox
4. Push rider entry into `pool_queue:{regionId}` sorted set (TTL = `match_timeout_secs`)
5. Return `202 Accepted` `{ ride_id, status: "pool_queued", match_timeout_secs, discounted_fare_estimate }`

### Pool Matcher (`PoolMatcherService` — Kafka consumer on `ride_pool_queued`)

1. Scan `pool_queue:{regionId}` for the oldest waiting entry
2. Compute route detour — skip candidate if detour > `max_detour_km`
3. **If compatible match found:**
   a. Atomically remove both from Redis queue (Lua script — prevents double-match race)
   b. Generate `pool_trip_id` (UUID)
   c. `UPDATE rides SET pool_slot=1/2, pool_trip_id=X, status='pool_matched', version=version+1` (optimistic concurrency)
   d. Run existing driver matching score algorithm, filtered to pool-capable drivers with open slot
   e. Write `ride_pool_matched` event to outbox for both ride IDs
4. **If no match:** leave in queue; TTL handles expiry

### Timeout Flow

1. Scheduled sweep job runs every 30s — detects expired `pool_queued` rides (past `match_timeout_secs`)
2. Emit `ride_pool_timeout` event; notify rider via SignalR: *"No match found — accept solo fare?"*
3. Rider responds via `POST /v1/rides/{id}/accept-solo-upgrade` within 60s → ride re-enters solo matching (`status=requested`)
4. No response or decline → `status=cancelled`, `ride_cancelled` event emitted

---

## API

### Extended endpoint (non-breaking)

```
POST /v1/rides/request
Body: { origin, destination, ride_type: "solo" | "pool" }   -- ride_type defaults to "solo"

Response (pool):
  202 Accepted
  { ride_id, status: "pool_queued", match_timeout_secs, discounted_fare_estimate }
```

### New endpoint

```
POST /v1/rides/{id}/accept-solo-upgrade
  202 Accepted { status: "requested" }   -- re-enters standard solo matching
```

### Existing `GET /v1/rides/{id}`

Returns `pool_slot` and `pool_trip_id` for pool rides. **Never returns the other rider's pickup/dropoff coordinates.**

---

## Kafka Event Contracts

| Event | Payload |
|---|---|
| `ride_pool_queued` | `{ ride_id, rider_id, region_id, origin, destination, requested_at }` |
| `ride_pool_matched` | `{ pool_trip_id, ride_ids:[id1,id2], driver_id, region_id, pickup_order }` |
| `ride_pool_timeout` | `{ ride_id, rider_id, region_id, reason: "no_match", notify_user: true }` |
| `ride_pool_upgraded` | `{ ride_id, rider_id, region_id, previous_status: "pool_queued" }` |
| `ride_partial_dropoff` | `{ pool_trip_id, completed_ride_id, remaining_ride_id, region_id }` |

All existing `ride_*` events (e.g. `ride_matched`, `ride_completed`) continue to fire per-ride as before. Existing consumers are unaffected.

Topics follow existing region scoping: `ride-events-{region}`, DLQ: `ride-events-dlq-{region}`.

---

## Error Handling

| Scenario | Handling |
|---|---|
| Double-match race (two matchers pick same rider) | Lua atomic remove from Redis; loser retries with next candidate |
| No pool-capable drivers in region | `202 pool_queued`; fallback prompt after timeout |
| Rider 2 cancels after `pool_matched` | Rider 1 continues as solo; compensation event emitted; driver re-evaluated |
| Driver cancels mid pool trip | Both rides re-enter matching; both riders notified via SignalR |
| `pool_region_rates` missing for region | `400 Bad Request` — pool not available in this region |
| Optimistic concurrency conflict on ride update | `409 Conflict` with `RESOURCE_CONFLICTED`; matcher retries with fresh version |

---

## Observability

### Log fields (all pool events add)

`TraceId`, `SpanId`, `RegionId`, `RideId`, `PoolTripId`, `PoolSlot`

| Level | Events |
|---|---|
| Info | `pool_queued`, `pool_matched`, `partial_dropoff`, `pool_completed` |
| Warning | `pool_timeout`, solo upgrade accepted/declined |
| Error | Lua atomic failure, matcher consumer DLQ, double-match collision |

### Metrics

| Metric | Purpose |
|---|---|
| `pool_queue_depth_{regionId}` | Alert on queue backup — low driver supply signal |
| `pool_match_rate` | % of pool requests successfully matched |
| `pool_timeout_rate` | % falling back to solo or cancelling |
| `pool_match_latency_ms` | Time from `pool_queued` → `pool_matched` |
| `pool_detour_km` | Histogram of actual detour added per trip |

---

## Testing

### Unit Tests (Moq)

- `PoolMatcherService`: compatible match found, detour exceeded, no candidates
- Fare calculation: discount correctly applied from `pool_region_rates`
- State machine: valid transitions pass, invalid transitions rejected (e.g. `pool_queued → completed` is illegal)

### Integration Tests (Testcontainers — Postgres + Redis + Kafka)

- **Happy path:** 2 riders request pool → matched → driver assigned → both complete
- **Timeout flow:** 1 rider queued, TTL expires → SignalR nudge → solo upgrade accepted
- **Race condition:** 2 matcher instances attempt same pair simultaneously → only one succeeds
- **Rider 2 cancel:** Rider 1 continues as solo after Rider 2 cancels post-match
- **Driver cancel mid-trip:** Both riders re-enter matching queue

### Privacy Guard

- Assert `GET /v1/rides/{id}` for Rider A **never** returns Rider B's pickup/dropoff coordinates

---

## Pre-Implementation Checklist

- [ ] `/v1/` versioning on all new endpoints
- [ ] `ride_views` not written to directly — Kafka consumer only
- [ ] All Kafka events via `IOutboxPublisher`
- [ ] `version` check on all `rides` updates (optimistic concurrency)
- [ ] `PoolTripId` + `PoolSlot` in all pool log entries
- [ ] Redis GEO TTLs and stale-driver cleanup unaffected by pool changes
- [ ] Kafka DLQ fallback defined for `PoolMatcherService` consumer
- [ ] `CancellationToken` propagated in all async matcher methods
