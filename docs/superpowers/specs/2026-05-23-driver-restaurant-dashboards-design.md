# Driver & Restaurant Dashboards Design

**Date:** 2026-05-23  
**Status:** Approved  
**Module:** Gruuber.Analytics (new dedicated module)  
**Approach:** Time-Bucketed Daily Snapshot Tables, Kafka-populated via `AnalyticsConsumerService`

---

## Overview

A dedicated `Gruuber.Analytics` module provides pre-aggregated, read-only dashboard data for three roles: drivers, restaurant owners, and internal admins. Data is populated exclusively by Kafka consumers reacting to domain events — no direct writes from web request handlers. Weekly and monthly views are derived at query time by summing daily snapshot rows.

Exports (CSV and PDF) are generated asynchronously via a background job, with clients polling for a download URL.

---

## Constraints & Decisions

| Decision | Choice |
|---|---|
| Aggregation strategy | Time-bucketed daily snapshot tables (not materialized views) |
| Data freshness | Near-real-time — updated per Kafka event |
| Time periods | Daily, weekly, monthly |
| Export formats | CSV and PDF (async job, polled via job_id) |
| Module boundary | Dedicated `Gruuber.Analytics` module with its own read models |
| Authorization | Role-scoped; entity ID always from JWT `sub` — never from query params |

---

## Data Model

### `driver_stats_daily`

```sql
CREATE TABLE driver_stats_daily (
  driver_id        UUID NOT NULL,
  region_id        INT NOT NULL,
  stat_date        DATE NOT NULL,
  trips_completed  INT DEFAULT 0,
  trips_cancelled  INT DEFAULT 0,
  pool_trips       INT DEFAULT 0,
  gross_earnings   NUMERIC DEFAULT 0,
  bonus_earnings   NUMERIC DEFAULT 0,
  payout_amount    NUMERIC DEFAULT 0,
  avg_rating       NUMERIC DEFAULT 0,
  acceptance_rate  NUMERIC DEFAULT 0,  -- 0.0–1.0
  online_minutes   INT DEFAULT 0,
  PRIMARY KEY (driver_id, stat_date)
);
```

### `restaurant_stats_daily`

```sql
CREATE TABLE restaurant_stats_daily (
  restaurant_id    UUID NOT NULL,
  region_id        INT NOT NULL,
  stat_date        DATE NOT NULL,
  orders_received  INT DEFAULT 0,
  orders_completed INT DEFAULT 0,
  orders_cancelled INT DEFAULT 0,
  gross_revenue    NUMERIC DEFAULT 0,
  avg_prep_time_secs INT DEFAULT 0,
  avg_rating       NUMERIC DEFAULT 0,
  PRIMARY KEY (restaurant_id, stat_date)
);
```

### `menu_item_stats_daily`

```sql
CREATE TABLE menu_item_stats_daily (
  restaurant_id  UUID NOT NULL,
  item_name      TEXT NOT NULL,   -- sourced from order_items.item_name
  stat_date      DATE NOT NULL,
  units_sold     INT DEFAULT 0,
  revenue        NUMERIC DEFAULT 0,
  PRIMARY KEY (restaurant_id, item_name, stat_date)
);
```

### `admin_stats_daily`

```sql
CREATE TABLE admin_stats_daily (
  region_id               INT NOT NULL,
  stat_date               DATE NOT NULL,
  total_rides             INT DEFAULT 0,
  total_pool_rides        INT DEFAULT 0,
  total_orders            INT DEFAULT 0,
  gross_platform_revenue  NUMERIC DEFAULT 0,
  active_drivers          INT DEFAULT 0,
  active_restaurants      INT DEFAULT 0,
  PRIMARY KEY (region_id, stat_date)
);
```

### `analytics_export_jobs`

```sql
CREATE TABLE analytics_export_jobs (
  job_id       UUID PRIMARY KEY,
  owner_id     UUID NOT NULL,     -- driver_id or restaurant_id or admin user id
  role         TEXT NOT NULL,     -- 'driver' | 'restaurant' | 'admin'
  format       TEXT NOT NULL,     -- 'csv' | 'pdf'
  status       TEXT NOT NULL DEFAULT 'pending',  -- pending | processing | completed | failed
  from_date    DATE NOT NULL,
  to_date      DATE NOT NULL,
  download_url TEXT,              -- pre-signed URL, set on completion
  expires_at   TIMESTAMP,         -- pre-signed URL expiry
  created_at   TIMESTAMP NOT NULL DEFAULT now()
);
```

### `processed_analytics_events`

```sql
CREATE TABLE processed_analytics_events (
  event_id UUID PRIMARY KEY,
  processed_at TIMESTAMP NOT NULL DEFAULT now()
);
```

Used for Kafka consumer idempotency — duplicate events are skipped if `event_id` is already present.

---

## Kafka Consumer — `AnalyticsConsumerService`

Listens to all region-scoped topics. All upserts use:

```sql
INSERT INTO <table> (...) VALUES (...)
ON CONFLICT (pk) DO UPDATE SET col = col + delta;
```

### Event → Table Mapping

| Kafka Event | Tables Updated |
|---|---|
| `ride_completed` | `driver_stats_daily` (driver), `admin_stats_daily` (region) |
| `ride_cancelled` | `driver_stats_daily` (trips_cancelled), `admin_stats_daily` |
| `ride_rated` | `driver_stats_daily` (avg_rating rolling recalc) |
| `order_delivered` | `restaurant_stats_daily`, `menu_item_stats_daily` (per item), `admin_stats_daily` |
| `order_cancelled` | `restaurant_stats_daily` (orders_cancelled) |
| `order_rated` | `restaurant_stats_daily` (avg_rating rolling recalc) |
| `driver_went_online` / `driver_went_offline` | `driver_stats_daily` (online_minutes delta) |
| `payment_success` | `driver_stats_daily` (payout_amount), `admin_stats_daily` (gross_platform_revenue) |

### Idempotency

Before any upsert, the consumer checks `processed_analytics_events` for the `event_id`. If found, the event is skipped. If not, the upsert proceeds and the `event_id` is inserted atomically in the same DB transaction.

### Retry & DLQ

- Max 5 retries with exponential backoff + jitter (`IExponentialBackoff`)
- On 5th failure: route to `analytics-events-dlq-{region}`
- Stats will be temporarily understated — not incorrect — until DLQ is replayed

---

## API

All endpoints are under `Gruuber.Analytics`. All require a valid JWT. Entity IDs are always extracted from JWT `sub` — never accepted as query/path parameters for scoped roles.

### Driver Dashboard

```
GET /v1/analytics/driver/summary?period=daily|weekly|monthly
  → 200 { trips_completed, trips_cancelled, pool_trips, gross_earnings,
           bonus_earnings, payout_amount, avg_rating, acceptance_rate, online_minutes }

GET /v1/analytics/driver/trips?page=1&limit=20&from=YYYY-MM-DD&to=YYYY-MM-DD
  → 200 { items: [...], total, page, limit }

GET /v1/analytics/driver/earnings/export?format=csv|pdf&from=YYYY-MM-DD&to=YYYY-MM-DD
  → 202 Accepted { job_id }

GET /v1/analytics/driver/exports/{job_id}
  → 200 { status: "completed", download_url, expires_at }
  → 202 { status: "processing" }
  → 200 { status: "failed" }
```

### Restaurant Dashboard

```
GET /v1/analytics/restaurant/summary?period=daily|weekly|monthly
  → 200 { orders_received, orders_completed, orders_cancelled,
           gross_revenue, avg_prep_time_secs, avg_rating, cancellation_rate }

GET /v1/analytics/restaurant/orders?page=1&limit=20&from=YYYY-MM-DD&to=YYYY-MM-DD
  → 200 { items: [...], total, page, limit }

GET /v1/analytics/restaurant/menu-performance?period=weekly|monthly
  → 200 { items: [{ item_name, units_sold, revenue }] sorted by units_sold desc }

GET /v1/analytics/restaurant/revenue/export?format=csv|pdf&from=YYYY-MM-DD&to=YYYY-MM-DD
  → 202 Accepted { job_id }

GET /v1/analytics/restaurant/exports/{job_id}
  → same pattern as driver export
```

### Admin Dashboard

```
GET /v1/analytics/admin/summary?region_id=&period=daily|weekly|monthly
  → 200 { total_rides, total_pool_rides, total_orders,
           gross_platform_revenue, active_drivers, active_restaurants }

GET /v1/analytics/admin/export?format=csv|pdf&region_id=&from=YYYY-MM-DD&to=YYYY-MM-DD
  → 202 Accepted { job_id }

GET /v1/analytics/admin/exports/{job_id}
  → same pattern as driver export
```

---

## Authorization

| Role | JWT Scope | Access |
|---|---|---|
| `driver` | `analytics.driver.read` | Own data only — `driver_id` from JWT `sub` |
| `restaurant` | `analytics.restaurant.read` | Own restaurant only — `restaurant_id` from JWT `sub` |
| `admin` | `analytics.admin.read` | All regions — optional `?region_id=` filter |

Export download URLs are pre-signed with a short TTL. Expired URLs return `403` or `410 Gone`.

---

## Error Handling

| Scenario | Handling |
|---|---|
| No stats for requested period | `200 OK` with zero-valued summary — never `404` |
| Invalid date range (`from > to`) | `400 Bad Request` with descriptive error |
| Export `job_id` not found | `404 Not Found` |
| Export generation fails | Job status → `failed`; client re-requests a new export |
| Duplicate Kafka event | Skipped via `processed_analytics_events` dedup check |
| Consumer DLQ after 5 retries | Alert ops; stats temporarily understated until replay |
| Unauthorized access (wrong role or wrong entity) | `403 Forbidden` |

---

## Observability

### Log Fields

All analytics log entries include: `TraceId`, `SpanId`, `RegionId`, and where applicable `DriverId`, `RestaurantId`, `ExportJobId`.

| Level | Events |
|---|---|
| Info | Stats upsert completed, export job started/completed |
| Warning | Duplicate event skipped, export job retried |
| Error | Upsert failure, DLQ routed, export job failed |

### Metrics

| Metric | Purpose |
|---|---|
| `analytics_consumer_lag_{region}` | Alert if consumer falls behind — dashboards go stale |
| `analytics_upsert_errors` | DB upsert failure rate |
| `export_job_duration_ms` | P95 target < 5s for typical date ranges |
| `export_job_failures` | Failed CSV/PDF generation rate |

---

## Testing

### Unit Tests (Moq)

- `AnalyticsConsumerService`:
  - `ride_completed` → correct delta upserted to `driver_stats_daily` and `admin_stats_daily`
  - `order_delivered` → correct upsert across restaurant + menu item + admin tables
  - Duplicate `event_id` → second upsert skipped (idempotency enforced)
  - Rolling average rating recalculation is mathematically correct
- Query Handlers:
  - Weekly summary correctly sums 7 daily rows
  - Monthly summary correctly sums up to 31 daily rows
  - No data for period → returns zero-valued summary (not null/404)
- Export Service:
  - CSV output matches expected columns and row count
  - Job status transitions: `pending → processing → completed / failed`

### Integration Tests (Testcontainers — Postgres + Kafka)

- Publish 5 `ride_completed` events → assert `driver_stats_daily` has correct cumulative totals
- Publish `order_delivered` with 3 items → assert 3 rows in `menu_item_stats_daily`
- Replay duplicate event → totals unchanged
- Consumer fails 5 times → message lands in `analytics-events-dlq-{region}`
- Export job: request CSV → poll `job_id` → download URL returns valid file with correct content

### Authorization Tests

- Driver A cannot access Driver B's summary (JWT `sub` mismatch → `403`)
- Restaurant role cannot access `/v1/analytics/driver/*` → `403`
- Admin can query any region; driver/restaurant cannot use `/v1/analytics/admin/*` → `403`
- Expired export download URL → `403` / `410 Gone`

---

## Pre-Implementation Checklist

- [ ] All endpoints under `/v1/analytics/`
- [ ] Entity IDs sourced from JWT `sub` — never from request params for scoped roles
- [ ] `ride_views` / `orders` write tables not touched by Analytics module
- [ ] All Kafka consumers use `IExponentialBackoff` with max 5 retries + DLQ
- [ ] `processed_analytics_events` dedup checked inside same DB transaction as upsert
- [ ] Export download URLs are pre-signed with short TTL
- [ ] `ExportJobId` included in all export log entries
- [ ] `CancellationToken` propagated in all async consumer and export methods
- [ ] `analytics_consumer_lag_{region}` metric wired to alerting
