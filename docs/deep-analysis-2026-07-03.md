# Gruuber — Deep Analysis & Re-work Plan (2026-07-03)

Scope: production-readiness gaps, feature completeness vs `spec_1_uber_food_modular_monolith.md`, and test coverage quality.
Baseline: `main` @ `108de55`. Test suite: 165 passed / 0 failed / 22 skipped.

---

## Verdict

The architecture skeleton (modular monolith, CQRS folders, outbox, workers, per-region topics) is genuinely in place and the newer feature modules (Chat, Analytics, Pooling handlers) are the best-built parts. But **the core money paths (rides, orders, payments) have broken authorization, client-dictated amounts, and several dead-ended flows**, and the test suite is inverted — it heavily covers design-pattern scaffolding that production code never executes, while the ride/order/payment lifecycle has zero tests.

---

## P0 — Broken or exploitable (fix before anything else)

### 1. Rate limiter runs before authentication → everyone is "anonymous"
[Program.cs:144](../src/Gruuber.Api/Program.cs#L144) puts `RedisRateLimiterMiddleware` **before** `UseAuthentication()`. JWT claims are not populated yet, so `context.User` has no role/sub → every request lands in the anonymous bucket (20 req/min) keyed by IP. Riders never get 100/min, drivers never get 300/min, and a driver's 2–3s location heartbeat alone (~20–30 req/min) exhausts the shared IP bucket. **Fix: move the middleware after `UseAuthentication()`.** (One-line fix; add a test.)

### 2. Ride Pooling is unreachable through the API
`RequestRideRequest` ([RidesController.cs:87](../src/Gruuber.Api/Controllers/RidesController.cs#L87)) has no `DestLat`/`DestLng` fields and the controller never passes them, so `RequestRideCommand.DestLat/DestLng` are always null → every `ride_type=pool` request fails with `DEST_REQUIRED`. Side effect: **solo rides never persist a destination either.** Handler-level unit tests pass because they construct the command directly, masking the controller gap.

### 3. Missing object-level authorization (IDOR) across all core endpoints
- `PATCH /v1/rides/{id}/status` — `[Authorize]` (any role); handler never checks the actor against `ride.RiderId`/`ride.DriverId`. Anyone with an account can cancel or complete anyone's ride.
- `PATCH /v1/orders/{id}/status` — same; a rider can mark their own order `delivered`, a stranger can cancel it.
- `POST /v1/payments/{id}/confirm|fail` — any driver can confirm/fail any payment; `RegionId` comes from the request body, not JWT.
- `GET /v1/rides/{id}`, `/v1/orders/{id}`, `/v1/payments/{id}` — any authenticated user can read anyone's records.
- **LocationHub** ([LocationHub.cs](../src/Gruuber.Api/Hubs/LocationHub.cs)) — `JoinRideGroup(anyRideId)` has no membership check → live GPS feed of any ride is public to any account.
- **ChatHub.JoinThread** ([ChatHub.cs:22](../src/Gruuber.Chat/Hubs/ChatHub.cs#L22)) — adds caller to the group and mutates delivery status **before** checking participation (`SendMessage` checks; `JoinThread` doesn't) → non-participants can listen to any thread.
- Tracking heartbeat: `ActiveRideId` is client-supplied — a driver can broadcast coordinates into any ride's group.

### 4. Monetary integrity — clients dictate prices
- `POST /v1/orders/create` accepts `Price` per item from the client; there is no menu/catalog to validate against → order totals are arbitrary.
- `POST /v1/payments` accepts `Amount` from the client; never reconciled against `ride.FinalFare` or the order total; the ride isn't even verified to exist or belong to the rider.
- No idempotency key on payment initiation (spec lists idempotency keys as a reliability pattern) → double-tap = duplicate payments.
- `Payment.TryConfirm/TryFail` guard only the version, not the current status → a `Failed` payment can later be `Succeeded`.

### 5. Payment lifecycle is dead-ended
- `PaymentTimeoutWorker` polls for `Status == PendingConfirmation`, but **nothing ever sets that status** (`Create` → `Initiated`, confirm → `Succeeded`, fail → `Failed`). The whole 15-minute timeout/refund path can never trigger.
- The spec's saga leg `payment_success → Order updated to paid` does not exist: **the Orders module has no Kafka consumer at all.** Orders never learn about payments; `payment_timeout` refund events have no consumer anywhere.

---

## P1 — Architectural drift & half-built features

### 6. Kafka ordering: events for one ride can arrive out of order
`OutboxWorker` publishes with `key = entry.Id` (a fresh GUID per event) → events for the same ride hash to different partitions. `RideViewConsumer` silently drops `ride_status_changed` for rides it hasn't seen (`driver_matched` may arrive later). **Key messages by `RideId`/`OrderId`.**

### 7. `ride_views` read model is only half-maintained
The consumer handles just `driver_matched` and `ride_status_changed`. `DriverName` is never populated, `Lat`/`Lng` never updated, no row is created on `ride_requested`. So `GET /v1/rides/{id}` (which prefers the view) returns an empty driver name and `0,0` coordinates. Either feed the view (driver profile lookup + location events) or slim it to what's actually maintained.

### 8. Restaurant workflow (spec **Must Have**) is missing
No `restaurants` entity/table (the spec schema defines one with an FK from orders), no menu, no onboarding or open/closed state, no restaurant-facing endpoints beyond the shared order-status PATCH. `RestaurantId` on an order is an unvalidated GUID. Also `CreateOrderRequest` requires a `RideId` at placement — a food order shouldn't need a pre-existing ride; delivery assignment belongs later in the lifecycle.

### 9. Pattern scaffolding is dead code presented as implemented
- **MediatR**: referenced in every csproj, `AddMediatR` never called, no `IRequestHandler` implementations. The three pipeline behaviors (Logging/Validation/ErrorHandling) never execute. The checklist's "MediatR wired in all modules" is false.
- **Unit of Work** (`RidesUnitOfWork`, `OrdersUnitOfWork`): never registered or used — handlers open transactions inline.
- **Specifications** (`DriverMatchEligibilitySpecification`, `OrderEligibilitySpecification` incl. restaurant-open and min-basket rules): never consulted by any handler — real validation logic exists only in tests.
- **Builders**, **Memento snapshots**, **Multiton registries** (`RegionedRedisDatabaseRegistry`, `RegionedKafkaProducerRegistry`): defined, tested, unused/unregistered.
Decide per pattern: wire it into the real path or delete it. Shipping both a bespoke path and an unused "pattern" path is the worst option.

### 10. Surge pricing: spec/impl mismatch
Spec describes `SurgeMultiplierConsumer` maintaining Redis demand/supply counters plus admin override endpoints (`POST/DELETE /v1/surge/{regionId}/override`). Implementation counts demand via live DB queries per request and exposes different endpoints (`GET /v1/surge/estimate`, `PUT /v1/admin/surge/config`); no override capability. Functional, but reconcile the spec or the code, and note the DB-count approach adds write-DB load on every ride/order creation.

### 11. YARP gateway is an empty shell
`ReverseProxy.Routes/Clusters` are `{}` in appsettings. No region routing, no gateway responsibilities. Harmless in a monolith, but the spec attributes auth/routing/rate limiting to YARP — update the spec or remove the dead config/package.

### 12. Observability falls short of CLAUDE.md
- OTel exports **to console only**; the `OpenTelemetry:Endpoint` setting is read by nobody. No EF Core, Redis, or Kafka instrumentation; no trace propagation via Kafka headers.
- No metrics (`request_count`, `request_duration_ms`, `KafkaConsumerFailures`, `PaymentTimeouts`).
- Readiness checks Postgres/Redis/Kafka broker, but not "Kafka consumer active" as CLAUDE.md requires.
- SignalR location broadcast is per-heartbeat (2–3s), not throttled/state-change-only (CLAUDE.md 9.3).

### 13. Operational hygiene gaps
- Outbox tables grow forever (processed rows never pruned); no ride archiving (90 days) or nightly `ride_views` pruning (CLAUDE.md 12).
- `OutboxWorker` is three copy-pasted ~55-line blocks — one generic method over `IEventMessageFactory`/entry type would collapse it.
- No CI: `.github/workflows/` is empty despite the stated Build → Test → Deploy pipeline.
- No login throttling/lockout (compounded by the broken rate limiter).

### 14. Real-time delivery is location-only — no status push, no backplane
- `SignalRLocationBroadcaster` pushes **location updates only**. Nothing broadcasts ride/order lifecycle changes (`driver_matched`, `arrived`, order `ready`, …) to clients — the spec's "notify client on assignment via SignalR" doesn't exist, so clients must poll `GET /v1/rides/{id}`.
- `AddSignalR()` is registered **without the Redis backplane** (`AddStackExchangeRedis`), though the spec claims Redis backplane for multi-instance fan-out. Single-instance-only real-time today.
- Hubs accept JWT only via `Authorization` header; there is no `access_token` query-string handling (`OnMessageReceived`), so browser WebSocket clients cannot authenticate to the hubs at all (native clients can).

### 15. Client-facing API surface has no list/collection endpoints
Every ride/order/payment read is by-ID only. There is no "my active ride", "my ride history", "my orders", or driver's "assigned rides" endpoint — a client app has no way to discover its own entity IDs after an app restart. (Chat is the exception: `GET /v1/chat/threads` exists with pagination.) There is also no profile endpoint (`GET /v1/me`) and no logout / token-revocation endpoint despite refresh tokens being revocable in the domain model.

---

## P2 — Test suite inversion

What's covered: design patterns (114 MSTest methods over largely dead code), Chat, Analytics, Pooling handlers, Surge service — the newest work.

**Zero tests** for: `RequestRideHandler` (solo), `DriverMatchCoordinator` (scoring, 409 conflict), `TransitionRideHandler`/`TransitionOrderHandler` (state machine + version), all payment handlers, `PaymentTimeoutWorker`, `OutboxWorker` (retry → DLQ), `RideViewConsumer`, all Auth handlers (login, refresh rotation/reuse), the rate limiter, `RedisGeoService`.

All 22 integration tests are `[Fact(Skip = "Requires Testcontainers…")]` stubs — the Testcontainers packages are referenced but no container fixture exists. The suite also mixes xUnit and MSTest in one project.

---

## Recommended plan

**Phase 1 — Security & correctness hotfixes (highest value, small diffs)**
1. Move rate limiter after `UseAuthentication()`; add regression test.
2. Add ownership/role checks: ride/order transitions (actor vs rider/driver + role-per-transition map), payment confirm/fail (must be the ride's driver or an internal role), GET endpoints scoped to owner/participant, LocationHub + ChatHub join guards, server-derived `RegionId` everywhere it's still read from request bodies.
3. Add `DestLat`/`DestLng` to the ride request DTO (fixes pooling + destination persistence).
4. Payment state guards (`TryConfirm` only from `Initiated`/`PendingConfirmation`, etc.) + idempotency key on initiation + amount validated against the ride/order fare.

**Phase 2 — Finish the sagas the spec promises**
5. Decide the payment confirmation model (mock provider webhook vs driver action), set `PendingConfirmation` where the spec says, making `PaymentTimeoutWorker` reachable.
6. Add an Orders-side consumer for `payment-events-*` (order → paid / payment-failed compensation), and a consumer (or explicit backlog decision) for `payment_timeout` refunds.
7. Key outbox Kafka messages by entity ID; enrich `RideViewConsumer` (create view on `ride_requested`, populate driver name, consume location updates) or slim the read model.
8. Broadcast ride/order status changes over SignalR (consumer → hub group) and add the Redis backplane so real-time survives scaling past one instance.

**Phase 3 — Restaurant vertical (the one Must Have that's missing)**
9. Restaurant entity + menu items (server-side prices kill the client-priced-order hole at the root), open/closed state wired into order eligibility (the specification classes already exist — use them), restaurant-scoped order queue endpoints. Drop the required `RideId` at order placement; assign delivery later.

**Phase 4 — Client-API completeness (prereq for any mobile/web client — see `mobile-app-analysis-2026-07-03.md`)**
10. List/collection endpoints: `GET /v1/rides?mine=active|history`, `GET /v1/orders?mine=…`, driver's assigned-ride lookup; `GET /v1/me` profile; `POST /v1/auth/logout` (refresh-token revocation).
11. Push-notification infrastructure (device-token registration + a Kafka-consuming dispatcher for FCM/APNs) and a driver offer/accept matching flow.

**Phase 5 — Debt & confidence**
12. Pattern reconciliation: wire MediatR + behaviors for real (CLAUDE.md says to use it) or remove the package and the dead behaviors; same call for UoW/builders/snapshots/multiton; delete or integrate specifications (Phase 3 uses two of them). Update `docs/design-patterns-checklist.md` to reality.
13. Test rebuild: unit tests for every Phase 1–2 fix; one real Testcontainers fixture (Postgres+Redis+Kafka) and un-skip the 22 integration stubs; consolidate on xUnit.
14. Observability: OTLP exporter honoring `OpenTelemetry:Endpoint`, EF/Redis/Kafka instrumentation + trace headers, consumer-liveness readiness check, broadcast throttling.
15. Ops: outbox pruning + ride archiving jobs, deduplicate `OutboxWorker`, add CI workflow (build + test), login lockout.

**Spec hygiene (parallel):** update spec/CLAUDE.md where reality is intentionally different (surge endpoints, YARP story) so the docs stop describing features that don't exist.
