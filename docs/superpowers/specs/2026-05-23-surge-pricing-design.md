# Surge Pricing Design

**Date:** 2026-05-23  
**Status:** Approved  
**Modules:** Gruuber.Rides, Gruuber.Orders (inline), new `SurgePricingService`  
**Approach:** Inline Fare Calculator with Redis-cached config (TTL 60s)

---

## Overview

Surge pricing applies a dynamic multiplier to the base fare at ride/order booking time. The multiplier is resolved inline by `SurgePricingService` using a combination of real-time demand/supply ratio and admin-configured time-of-day rules. The final fare is locked at the moment of booking — riders always pay what they were quoted.

Surge applies to both rides and food delivery orders, with independently configured multiplier caps per type. The multiplier is only surfaced to riders when it exceeds 1.0x — normal pricing shows no surge indicators.

---

## Constraints & Decisions

| Decision | Choice |
|---|---|
| Applies to | Rides and food delivery (separate caps per type) |
| Trigger | Demand/supply ratio + time-of-day rules (time rules take precedence) |
| Multiplier format | Stepped tiers with a hard cap per region/ride type |
| Fare quote | Upfront, locked at booking time — never changes post-booking |
| Rider confirmation | None — surge baked into quoted price |
| Surge display | Only shown to rider if multiplier > 1.0x |
| Recalculation | Per request, config loaded from Redis cache (TTL 60s) |

---

## Data Model

### `surge_config`

```sql
CREATE TABLE surge_config (
  region_id               INT NOT NULL,
  ride_type               TEXT NOT NULL,           -- 'ride' | 'food'
  demand_ratio_threshold  NUMERIC NOT NULL,         -- e.g. 0.5 = 50% drivers busy
  multiplier              NUMERIC NOT NULL,         -- e.g. 1.5
  max_multiplier          NUMERIC NOT NULL,         -- hard cap, e.g. 3.0
  updated_at              TIMESTAMP,
  PRIMARY KEY (region_id, ride_type, demand_ratio_threshold)
);
```

Multiple rows per `(region_id, ride_type)` define stepped tiers. Example:

| region_id | ride_type | demand_ratio_threshold | multiplier | max_multiplier |
|---|---|---|---|---|
| 1 | ride | 0.50 | 1.5 | 3.0 |
| 1 | ride | 0.75 | 2.0 | 3.0 |
| 1 | ride | 0.90 | 2.5 | 3.0 |
| 1 | food | 0.60 | 1.5 | 2.5 |

### `surge_time_rules`

```sql
CREATE TABLE surge_time_rules (
  id          UUID PRIMARY KEY,
  region_id   INT NOT NULL,
  ride_type   TEXT NOT NULL,
  day_of_week INT,               -- 0=Sun … 6=Sat; NULL = every day
  start_time  TIME NOT NULL,
  end_time    TIME NOT NULL,
  multiplier  NUMERIC NOT NULL,  -- overrides demand tier when active
  is_active   BOOLEAN NOT NULL DEFAULT true
);
```

### Schema Additions to `rides` and `orders`

```sql
ALTER TABLE rides ADD COLUMN base_fare        NUMERIC;
ALTER TABLE rides ADD COLUMN surge_multiplier NUMERIC NOT NULL DEFAULT 1.0;
ALTER TABLE rides ADD COLUMN final_fare       NUMERIC;       -- locked at booking
ALTER TABLE rides ADD COLUMN surge_reason     TEXT;          -- 'demand' | 'time_rule' | null

-- Same 4 columns added to orders table
ALTER TABLE orders ADD COLUMN base_fare        NUMERIC;
ALTER TABLE orders ADD COLUMN surge_multiplier NUMERIC NOT NULL DEFAULT 1.0;
ALTER TABLE orders ADD COLUMN final_fare       NUMERIC;
ALTER TABLE orders ADD COLUMN surge_reason     TEXT;
```

### Redis Keys

```
surge_config:{regionId}:{rideType}   TTL: 60s
→ Cached JSON of surge_config tiers + surge_time_rules for this region/type
→ Populated on first request after cache miss; invalidated immediately on admin config update
```

---

## Fare Calculation Flow

`SurgePricingService` is called inline during `POST /v1/rides/request` and `POST /v1/orders/create`.

### Multiplier Resolution Algorithm

1. Load config from Redis `surge_config:{regionId}:{rideType}` (TTL 60s).  
   On cache miss → query DB, populate cache.

2. Check `surge_time_rules` — is current server time within an active rule for this region/type?  
   **If YES** → `multiplier = time_rule.multiplier`, `reason = 'time_rule'` → skip step 3.

3. Compute demand/supply ratio:
   ```
   active_requests    = COUNT of rides/orders in 'requested' status for region
   available_drivers  = ZCARD driver_locations:{regionId}  (Redis GEO)
   ratio              = active_requests / max(available_drivers, 1)
   ```

4. Walk `surge_config` tiers descending by `demand_ratio_threshold`. Select the highest tier where `ratio >= threshold`.  
   `multiplier = min(tier.multiplier, max_multiplier)`, `reason = 'demand'`.  
   If no tier matches → `multiplier = 1.0`, `reason = null`.

5. Return `{ multiplier, reason, base_fare, final_fare = base_fare × multiplier }`.

### Fare Lock

The resolved fare is written in the **same DB transaction** as the ride/order `INSERT`:

```sql
INSERT INTO rides (..., base_fare, surge_multiplier, final_fare, surge_reason)
VALUES (..., :baseFare, :multiplier, :baseFare * :multiplier, :reason);
```

`final_fare` is immutable after this point — no subsequent event or config change can alter the rider's quoted price.

---

## API

### Extended endpoints (non-breaking)

```
POST /v1/rides/request
Response 202 Accepted:
{
  ride_id, status,
  fare_estimate: {
    base_fare,
    final_fare,
    surge_multiplier?,   -- only if > 1.0
    surge_reason?        -- only if > 1.0
  }
}

POST /v1/orders/create
Response 202 Accepted:
{
  order_id, status,
  fare_estimate: { base_fare, final_fare, surge_multiplier?, surge_reason? }
}

GET /v1/rides/{id}
→ Returns base_fare, final_fare, surge_multiplier, surge_reason on the ride object
```

### New endpoints

```
GET /v1/surge/estimate?region_id=&ride_type=ride|food
→ 200 { surge_multiplier, surge_reason, valid_for_secs: 30 }
  Allows clients to show surge indicator on booking screen before rider commits.

PUT /v1/admin/surge/config                    (admin role only)
→ Updates surge_config / surge_time_rules in DB
→ Immediately deletes Redis key surge_config:{regionId}:{rideType}
→ 200 OK
```

---

## Error Handling

| Scenario | Handling |
|---|---|
| Redis cache miss | Fall through to DB query, populate cache — request not blocked |
| Redis unavailable | Fall back to direct DB query — log Warning; no error surfaced to rider |
| No `surge_config` for region/type | Default `multiplier = 1.0` — ride proceeds at base fare |
| Multiplier exceeds `max_multiplier` | Clamped to `max_multiplier` — hard cap always enforced |
| `available_drivers = 0` | `ratio = active_requests / 1` — avoids divide-by-zero; highest tier triggers |
| Admin updates config mid-demand | Cache invalidated immediately; in-flight rides keep their locked `final_fare` |

---

## Observability

### Log Fields

All surge-related log entries include: `TraceId`, `SpanId`, `RegionId`, `RideId`/`OrderId`, `SurgeMultiplier`, `SurgeReason`.

| Level | Events |
|---|---|
| Info | Surge multiplier resolved (multiplier > 1.0), config cache refreshed |
| Warning | Redis unavailable — fell back to DB, cache miss rate elevated |
| Error | DB query failure during surge resolution |

### Metrics

| Metric | Purpose |
|---|---|
| `surge_active_{regionId}_{rideType}` | Current multiplier gauge — live view per region/type |
| `surge_cache_misses` | Frequency of Redis fallback to DB |
| `surge_rides_pct` | % of rides/orders booked at multiplier > 1.0x |
| `surge_calc_duration_ms` | P95 target < 10ms — must not impact request SLA |

---

## Testing

### Unit Tests (Moq)

- Demand ratio below all thresholds → `multiplier = 1.0`, no surge fields in response
- Demand ratio hits a tier → correct multiplier returned
- Multiplier exceeds `max_multiplier` → clamped correctly
- Active time rule → time rule multiplier used, demand ratio skipped
- Time rule inactive (outside window) → demand ratio evaluated normally
- Redis unavailable → falls back to DB without surfacing error
- No `surge_config` for region → multiplier defaults to 1.0

### Integration Tests (Testcontainers — Postgres + Redis)

- Book ride during active time rule → `final_fare = base × rule_multiplier`, locked in DB
- Book ride at high demand → correct tier multiplier applied and persisted on ride row
- Admin updates config → Redis key deleted → next request uses new config from DB
- `GET /v1/surge/estimate` returns multiplier matching subsequent booking result
- `available_drivers = 0` → no divide-by-zero, highest tier triggers correctly

### Fare Lock Invariant

- Assert: `ride.final_fare` is unchanged after admin updates surge config post-booking
- Assert: `order.final_fare` is unchanged after a new surge tier activates post-booking

---

## Pre-Implementation Checklist

- [ ] `/v1/` versioning on all new endpoints
- [ ] `SurgePricingService` called inline — not in a background worker
- [ ] `final_fare` written in same DB transaction as ride/order INSERT
- [ ] Redis fallback to DB on unavailability — no error surfaced to rider
- [ ] `surge_multiplier` and `surge_reason` omitted from response when multiplier = 1.0
- [ ] Admin config update immediately invalidates Redis key
- [ ] `SurgeMultiplier` and `SurgeReason` included in all relevant log entries
- [ ] `surge_calc_duration_ms` monitored — P95 < 10ms
- [ ] `CancellationToken` propagated through all async resolution methods
