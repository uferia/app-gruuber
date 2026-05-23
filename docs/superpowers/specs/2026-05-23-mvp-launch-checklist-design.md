# MVP Launch Checklist Fixes — Design Spec

**Date:** 2026-05-23  
**Scope:** All 22 items from the MVP launch checklist (14 MUST-FIX + 8 SHOULD-FIX)  
**Approach:** Wave-based parallel sub-agent execution; Wave 1 reviewed before Wave 2 begins.

---

## Problem Statement

The Gruuber codebase has a full feature set but several security, correctness, reliability, and observability gaps that must be resolved before production launch. This spec defines exactly what to change, where, and why for each of the 22 checklist items.

---

## Execution Structure

### Wave 1 — MUST-FIX (14 items, 3 parallel threads)

| Thread | Concern | Item count |
|---|---|---|
| 1A | Security | 7 |
| 1B | Correctness | 4 |
| 1C | Reliability | 3 |

### Wave 2 — SHOULD-FIX (8 items, 2 parallel threads)

| Thread | Concern | Item count |
|---|---|---|
| 2A | Observability + API completeness | 6 |
| 2B | Rate limiting + Validation | 2 |

---

## Wave 1 — Thread 1A: Security

### 1. IDOR/BOLA — Bind identity from JWT claims

**Problem:** Every controller accepts `RiderId`, `DriverId`, `RegionId` from the request body. A caller can impersonate any user.

**Fix:**
- Create `ICurrentUserContext` interface and `CurrentUserContext` implementation in `Gruuber.SharedKernel/Infrastructure/`.
- `CurrentUserContext` reads `sub` (UserId), `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` (Role), and `region_id` from `IHttpContextAccessor.HttpContext.User.Claims`.
- Exposes: `Guid UserId`, `string Role`, `int RegionId`.
- Register as `services.AddHttpContextAccessor()` + `services.AddScoped<ICurrentUserContext, CurrentUserContext>()` in `Program.cs`.
- All controllers inject `ICurrentUserContext` and override command fields:
  - `RidesController.RequestRide`: `cmd.RiderId = _currentUser.UserId`, `cmd.RegionId = _currentUser.RegionId`
  - `RidesController.MatchRide` (new): `cmd.DriverId = _currentUser.UserId`
  - `TrackingController.UpdateLocation`: `cmd.DriverId = _currentUser.UserId`, `cmd.RegionId = _currentUser.RegionId`
  - `PaymentsController.Initiate`: `cmd.RiderId = _currentUser.UserId`, `cmd.RegionId = _currentUser.RegionId`
  - `OrdersController.CreateOrder`: `cmd.RiderId = _currentUser.UserId`, `cmd.RegionId = _currentUser.RegionId`
  - `OrdersController.TransitionStatus`: `cmd.RegionId = _currentUser.RegionId`

### 2. 401/403 mapping broken

**Problem:** `ApplicationResultExtensions.ToHttpResult` falls through all non-200/404/409 to `BadRequest`, including status codes 401 and 403.

**Fix:** Add explicit branches in the `switch` for failure path:
```csharp
401 => controller.Unauthorized(error),
403 => controller.Forbid(),
```
Insert before the `_ => controller.BadRequest(error)` fallback.

### 3. Role enforcement per action

**Problem:** All protected controllers use bare `[Authorize]`, allowing any authenticated role to call any endpoint.

**Fix:** Replace `[Authorize]` with role-specific policies per action:

| Controller | Action | Policy |
|---|---|---|
| `RidesController` | `RequestRide` | `rider` |
| `RidesController` | `MatchRide` (new) | `driver` |
| `RidesController` | `GetRideStatus` | `[Authorize]` (any authenticated) |
| `RidesController` | `TransitionStatus` (new) | `driver` for en_route/arrived/completed; `rider` for cancelled |
| `OrdersController` | `CreateOrder` | `rider` |
| `OrdersController` | `TransitionStatus` | `driver` or `restaurant` |
| `OrdersController` | `GetOrder` | `[Authorize]` |
| `OrdersController` | `GetOrderItems` (new) | `[Authorize]` |
| `PaymentsController` | `Initiate` | `rider` |
| `PaymentsController` | `Confirm` | `driver` |
| `PaymentsController` | `Fail` | `driver` |
| `PaymentsController` | `GetPayment` (new) | `[Authorize]` |
| `TrackingController` | `UpdateLocation` | `driver` |

### 4. Swagger disabled in Production

**Problem:** Swagger middleware is unconditional, exposing the API schema in all environments.

**Fix:** Wrap in `Program.cs`:
```csharp
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Gruuber API v1"));
}
```

### 5. HTTPS hardening

**Problem:** No HTTPS redirection or HSTS headers.

**Fix:** In `Program.cs`, before `app.UseAuthentication()`:
```csharp
if (!app.Environment.IsDevelopment())
    app.UseHsts();
app.UseHttpsRedirection();
```

### 6. No user registration endpoint

**Problem:** There is no way to create users — `DevDataSeeder` is the only path today.

**Fix:**
- Add `RegisterCommand(string Email, string Password, string Role, int RegionId)` and `RegisterResponse(Guid UserId)` to `Gruuber.Auth.Application`.
- Add `RegisterHandler` in `Gruuber.Auth.Application.Commands`:
  - Validates `Role` is one of `rider`, `driver`, `restaurant`.
  - Checks for duplicate email (409 if exists).
  - Creates `User` with `BCrypt.HashPassword`, `IsActive = true`.
  - Saves and returns `RegisterResponse(user.Id)` with HTTP 201.
- Add `POST /v1/auth/register` to `AuthController` (no `[Authorize]` — public endpoint).
- Register `RegisterHandler` in `AuthModule`.

### 7. JWT secret minimum-length guard

**Problem:** A short or empty `Jwt:Secret` silently degrades HMAC-SHA256 security.

**Fix:** In `AuthModule.AddAuthModule`, after reading the secret:
```csharp
var secret = configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
if (secret.Length < 32)
    throw new InvalidOperationException("Jwt:Secret must be at least 32 characters.");
```
Use the validated `secret` variable in `SymmetricSecurityKey` construction.

---

## Wave 1 — Thread 1B: Correctness

### 8. Driver scoring uses (0, 0) coordinates

**Problem:** `DefaultDriverScoringService.GetScoredCandidatesAsync` hardcodes `lat=0, lng=0` when querying Redis GEO — all proximity scores are wrong.

**Fix:**
- Add `double PickupLat, double PickupLng` to `IDriverScoringService.GetScoredCandidatesAsync` signature.
- Update `DefaultDriverScoringService` to pass them to `_geo.GetNearbyDriversAsync(pickupLat, pickupLng, ...)`.
- Update `MatchDriverHandler.HandleAsync` to read `PickupLat`/`PickupLng` from the stored `Ride` entity.
- Add `PickupLat`/`PickupLng` columns to the `Ride` entity and `rides` table (new EF Core migration).
- `RequestRideHandler` already receives lat/lng in `RequestRideCommand` — persist them to `Ride`.
- `MatchDriverCommand` does not need lat/lng; handler reads them from the stored ride.

### 9. MatchDriverHandler has no controller route

**Problem:** `MatchDriverHandler` exists but is never reachable via HTTP.

**Fix:**
- Add `[HttpPost("{id:guid}/match")]` to `RidesController`.
- Request body: `MatchRideRequest(long ExpectedVersion)`.
- `DriverId` bound from JWT `sub` via `ICurrentUserContext`.
- `RegionId` bound from JWT `region_id` via `ICurrentUserContext`.
- Builds `MatchDriverCommand(id, request.ExpectedVersion, regionId)`.
- Protected by `[Authorize(Policy="driver")]`.

### 10. ride_views never populated

**Problem:** No Kafka consumer writes to `ride_views`, so `GET /v1/rides/{id}` always reads from the write model.

**Fix:**
- Add `RideViewConsumer : BackgroundService` in `Gruuber.Tracking.Application`.
- Consumes topic pattern `ride-events-{regionId}` for each region in `Kafka:RideRegions` config array using `Confluent.Kafka.IConsumer<string,string>` (already a transitive dependency via the existing producer).
- Deserializes JSON payload; handles events:
  - `driver_matched` → upsert `RideViewEntry { RideId, DriverId, Status = "matched", RegionId }`
  - `ride_status_changed` → update `Status` on existing `RideViewEntry`
- Uses `IExponentialBackoff` for retry; after 5 failures publishes to DLQ via `IKafkaProducer` and continues.
- Registered in `TrackingModule.AddTrackingModule`.
- Config keys added: `Kafka:BootstrapServers` (already needed for health check), `Kafka:ConsumerGroupId` (defaulting to `"gruuber-tracking"`), `Kafka:RideRegions` (int array, e.g. `[1, 2]`).

### 11. Redis TTL set on the whole key, not per member

**Problem:** `KeyExpireAsync` resets TTL on the entire `driver_locations:{regionId}` GEO key on every location update, evicting all drivers in the region at the time the most recently updated driver's TTL fires.

**Fix:** Replace whole-key TTL with a per-member heartbeat sorted set:
- Alongside `GeoAddAsync`, do `SortedSetAddAsync($"driver_ttl:{regionId}", driverId, unixNow + 10)`.
- In `GetNearbyDriversAsync`, before returning, prune members from both the GEO key and the TTL set where score < `unixNow` (use a Lua script for atomicity).
- Remove `KeyExpireAsync` from `AddDriverLocationAsync`.
- `RemoveDriverAsync` removes from both `driver_locations:{regionId}` (GeoRemove) and `driver_ttl:{regionId}` (SortedSetRemove).

---

## Wave 1 — Thread 1C: Reliability

### 12. DLQ entries never published

**Problem:** `OutboxWorker` marks entries `dlq` in the DB but never publishes them to a Kafka DLQ topic — dead events are silently dropped.

**Fix:** In `OutboxWorker`, when `entry.RetryCount >= MaxRetries`, publish to `{entry.EventType}-dlq` before setting `status = "dlq"`:
```csharp
await _producer.PublishAsync($"{entry.EventType}-dlq", entry.Id.ToString(), entry.Payload, cancellationToken);
entry.Status = "dlq";
```
Apply to all three `ProcessXOutboxAsync` methods. DLQ publish failure is logged as `Error` but does not block the status update.

### 13. PaymentTimeoutWorker timeout topic ignores region

**Problem:** Event type is hardcoded to `"payment-events-timeout"` instead of `"payment-events-timeout-{regionId}"`.

**Fix:** `Payment` entity inherits `RegionId` from `EntityBase`. Update the `PaymentOutboxEntry` creation in `PaymentTimeoutWorker`:
```csharp
EventType = $"payment-events-timeout-{payment.RegionId}",
```
Also add `refund_required = true` and `notify_user = true` fields to the payload per the spec.

### 14. RefreshToken.ExpiresAt hardcoded to 30 days

**Problem:** `RefreshToken.Create` hardcodes `AddDays(30)`, ignoring `Jwt:RefreshTokenTtlDays` config.

**Fix:**
- Add `int ttlDays` parameter to `RefreshToken.Create(userId, tokenHash, regionId, ttlDays)`.
- `LoginHandler` reads `_configuration.GetValue<int>("Jwt:RefreshTokenTtlDays", 30)` and passes it.
- `RefreshTokenHandler` (token rotation) does the same when creating the new token.
- `IConfiguration` already injected into `LoginHandler` via `IJwtTokenService` → extract config read directly in the handler.

> **Note:** `LoginHandler` currently doesn't inject `IConfiguration`. Add it to the constructor, or pass `ttlDays` from a new `IRefreshTokenSettings` interface. The simpler path is direct `IConfiguration` injection.

---

## Wave 2 — Thread 2A: Observability + API Completeness

### 15. No structured TraceId/SpanId at handler level

**Problem:** Logs are produced but without correlation IDs (TraceId, SpanId) in handlers.

**Fix:**
- Add NuGet package `Serilog.Enrichers.Span` to `Gruuber.Api`.
- Add `.Enrich.WithSpan()` to the Serilog configuration in `Program.cs`.
- This enriches every log line with `TraceId` and `SpanId` from `System.Diagnostics.Activity.Current` — no per-handler changes needed.

### 16. OpenTelemetry not configured

**Problem:** OpenTelemetry packages are referenced but `AddOpenTelemetry()` is absent.

**Fix:**
- Add NuGet packages: `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, `OpenTelemetry.Exporter.Console` (dev) / `OpenTelemetry.Exporter.OpenTelemetryProtocol` (prod).
- In `Program.cs`:
```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(b => b
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddConsoleExporter()); // swap for OtlpExporter in prod via config
```
- Add `OpenTelemetry:Endpoint` to `appsettings.json` (empty by default, used for OTLP in staging/prod).

### 17. Kafka health check missing from /health/readiness

**Problem:** `/health/readiness` checks Postgres and Redis but not Kafka — a dead Kafka broker is invisible.

**Fix:**
- Add NuGet package `AspNetCore.HealthChecks.Kafka`.
- Register in `Program.cs`:
```csharp
.AddKafka(
    new ProducerConfig { BootstrapServers = builder.Configuration["Kafka:BootstrapServers"] },
    name: "kafka",
    tags: new[] { "ready" })
```

### 18. No ride lifecycle transition endpoints

**Problem:** Rides have `en_route`, `arrived`, `completed`, `cancelled` states in the domain but no HTTP surface to reach them.

**Fix:**
- Add `TransitionRideRequest(string NewStatus, long ExpectedVersion)` record.
- Add `TransitionRideCommand(Guid RideId, RideStatus Next, long ExpectedVersion, int RegionId, Guid ActorId)`.
- Add `TransitionRideHandler` in `Gruuber.Rides.Application.Commands`:
  - Validates transition is allowed from current state.
  - Calls `ride.TryTransition(next, expectedVersion)`.
  - Writes `ride_status_changed` outbox event with `{ RideId, NewStatus, RegionId, OccurredAt }`.
  - Returns 409 on version conflict.
- Add `[HttpPatch("{id:guid}/status")]` to `RidesController`:
  - `[Authorize(Policy="driver")]` for `en_route`, `arrived`, `completed`.
  - `[Authorize(Policy="rider")]` for `cancelled`.
  - Bind `ActorId` from `ICurrentUserContext.UserId`, `RegionId` from `ICurrentUserContext.RegionId`.
- Register `TransitionRideHandler` in `RidesModule`.

### 19. No GET /v1/payments/{id}

**Problem:** Clients cannot poll payment state after `202 Accepted`.

**Fix:**
- Add `GetPaymentQuery(Guid PaymentId)`.
- Add `GetPaymentHandler` reading from `PaymentsDbContext.Payments`. Returns `PaymentDetailResponse(Id, RideId, Status, Amount, Currency, CreatedAt)`.
- Add `[HttpGet("{id:guid}")]` to `PaymentsController` with `[Authorize]`.
- Register `GetPaymentHandler` in `PaymentsModule`.

### 20. No GET /v1/orders/{id}/items

**Problem:** Order items are available via `GET /v1/orders/{id}` but the spec requires an explicit sub-resource endpoint.

**Fix:**
- Add `GetOrderItemsQuery(Guid OrderId)`.
- Add `GetOrderItemsHandler` querying `OrderItems` by `OrderId`. Returns `List<OrderItemDto>`.
- Add `[HttpGet("{id:guid}/items")]` to `OrdersController` with `[Authorize]`.
- Register `GetOrderItemsHandler` in `OrdersModule`.

---

## Wave 2 — Thread 2B: Rate Limiting + Validation

### 21. Redis-backed token bucket absent

**Problem:** No rate limiting on any endpoint — the spec calls for Redis token-bucket with role-based limits.

**Fix:**
- Implement `RedisRateLimiter` middleware in `Gruuber.Api.Infrastructure`:
  - Uses a Lua script for atomic token-bucket check: key `rate_limit:{role}:{userId}`, refill on each request based on role limits.
  - Role limits: rider = 100 req/min, driver = 300 req/min, restaurant = 200 req/min, unauthenticated = 20 req/min.
  - Location update endpoint (`POST /v1/drivers/location`) gets an additional 20 req/s bucket.
  - Returns 429 with `Retry-After` header on exhaustion.
- Register as `app.UseMiddleware<RedisRateLimiterMiddleware>()` in `Program.cs` before `app.UseAuthorization()`.
- Rate limits configurable via `RateLimiting:{Role}:RequestsPerMinute` config keys.

### 22. No input validation on any request model

**Problem:** Invalid JSON or missing fields cause unhandled exceptions rather than clean 400 responses.

**Fix:** Add data annotations to all request records:

```csharp
// AuthController
public record RegisterCommand(
    [Required][EmailAddress] string Email,
    [Required][MinLength(8)] string Password,
    [Required][RegularExpression("^(rider|driver|restaurant)$")] string Role,
    [Range(1, int.MaxValue)] int RegionId);

public record LoginCommand(
    [Required][EmailAddress] string Email,
    [Required] string Password);

// RidesController
public record RequestRideRequest(
    [Required] Guid RiderId,
    [Required][StringLength(64)] string RideType,
    [Range(-90, 90)] double PickupLat,
    [Range(-180, 180)] double PickupLng,
    [Range(1, int.MaxValue)] int RegionId);

// OrdersController
public record CreateOrderRequest(
    [Required] Guid RiderId,
    [Required] Guid RestaurantId,
    [Required] Guid RideId,
    [Range(1, int.MaxValue)] int RegionId,
    [Required][MinLength(1)] IList<OrderItemInput> Items);

// PaymentsController
public record InitiatePaymentRequest(
    [Required] Guid RideId,
    [Required] Guid RiderId,
    [Range(0.01, double.MaxValue)] decimal Amount,
    [Required][StringLength(3)] string Currency,
    [Range(1, int.MaxValue)] int RegionId);

// TrackingController
public record UpdateLocationRequest(
    [Required] Guid DriverId,
    [Range(-90, 90)] double Lat,
    [Range(-180, 180)] double Lng,
    [Range(1, int.MaxValue)] int RegionId,
    Guid? ActiveRideId);
```

`[ApiController]` already triggers automatic 400 model validation — no pipeline changes needed.

---

## File Change Summary

| File | Change type | Wave |
|---|---|---|
| `Gruuber.SharedKernel/Infrastructure/ICurrentUserContext.cs` | New | 1A |
| `Gruuber.SharedKernel/Infrastructure/CurrentUserContext.cs` | New | 1A |
| `Gruuber.Api/Extensions/ApplicationResultExtensions.cs` | Edit | 1A |
| `Gruuber.Api/Program.cs` | Edit (HTTPS, Swagger guard, register services, OTel, rate limiter, Kafka health) | 1A, 1B, 2A, 2B |
| `Gruuber.Api/Controllers/RidesController.cs` | Edit (role attrs, new match + lifecycle endpoints) | 1A, 1B, 2A |
| `Gruuber.Api/Controllers/AuthController.cs` | Edit (register endpoint) | 1A |
| `Gruuber.Api/Controllers/OrdersController.cs` | Edit (role attrs, new items endpoint) | 1A, 2A |
| `Gruuber.Api/Controllers/PaymentsController.cs` | Edit (role attrs, new GET endpoint) | 1A, 2A |
| `Gruuber.Api/Controllers/TrackingController.cs` | Edit (role attr, validation) | 1A, 2B |
| `Gruuber.Auth/AuthModule.cs` | Edit (JWT secret guard) | 1A |
| `Gruuber.Auth/Application/Commands/AuthCommands.cs` | Edit (RegisterCommand + RegisterResponse) | 1A |
| `Gruuber.Auth/Application/Commands/RegisterHandler.cs` | New | 1A |
| `Gruuber.Auth/Application/Commands/LoginHandler.cs` | Edit (pass ttlDays) | 1C |
| `Gruuber.Auth/Application/Commands/RefreshTokenHandler.cs` | Edit (pass ttlDays) | 1C |
| `Gruuber.Auth/Domain/RefreshToken.cs` | Edit (ttlDays param) | 1C |
| `Gruuber.Rides/Application/Commands/IDriverScoringService.cs` | Edit (add pickupLat/Lng params) | 1B |
| `Gruuber.Rides/Application/Commands/RideCommands.cs` | Edit (MatchDriverCommand no change needed) | 1B |
| `Gruuber.Rides/Application/Commands/MatchDriverHandler.cs` | Edit (read coords from ride) | 1B |
| `Gruuber.Rides/Application/Commands/TransitionRideHandler.cs` | New | 2A |
| `Gruuber.Rides/Application/Commands/RideCommands.cs` | Edit (add TransitionRideCommand) | 2A |
| `Gruuber.Rides/Domain/Ride.cs` | Edit (add PickupLat/Lng) | 1B |
| `Gruuber.Rides/Infrastructure/RidesDbContext.cs` | Edit (new migration for PickupLat/Lng) | 1B |
| `Gruuber.Rides/RidesModule.cs` | Edit (register TransitionRideHandler) | 2A |
| `Gruuber.Api/Infrastructure/DefaultDriverScoringService.cs` | Edit (pass pickup coords) | 1B |
| `Gruuber.Tracking/Infrastructure/RedisGeoService.cs` | Edit (per-member TTL via sorted set + Lua prune) | 1B |
| `Gruuber.Tracking/Application/RideViewConsumer.cs` | New | 1B |
| `Gruuber.Tracking/TrackingModule.cs` | Edit (register RideViewConsumer) | 1B |
| `Gruuber.Payments/Application/PaymentTimeoutWorker.cs` | Edit (region-scoped topic + extra payload fields) | 1C |
| `Gruuber.Payments/Application/Commands/GetPaymentHandler.cs` | New | 2A |
| `Gruuber.Payments/Application/Commands/PaymentCommands.cs` | Edit (add GetPaymentQuery) | 2A |
| `Gruuber.Payments/PaymentsModule.cs` | Edit (register GetPaymentHandler) | 2A |
| `Gruuber.Orders/Application/Queries/GetOrderItemsHandler.cs` | New | 2A |
| `Gruuber.Orders/Application/Queries/OrderQueries.cs` | Edit (add GetOrderItemsQuery) | 2A |
| `Gruuber.Orders/OrdersModule.cs` | Edit (register GetOrderItemsHandler) | 2A |
| `Gruuber.Api/Infrastructure/Kafka/OutboxWorker.cs` | Edit (publish to DLQ topic) | 1C |
| `Gruuber.Api/Infrastructure/RedisRateLimiterMiddleware.cs` | New | 2B |
| All controller request record types | Edit (add data annotations) | 2B |

---

## Constraints & Non-Goals

- No new ORM or SQL bypass — all DB changes via EF Core migrations.
- No MediatR introduction — existing handler-per-class pattern maintained.
- `ride_views` consumer is Kafka-based only — no synchronous write path to the read model.
- Rate limiter uses the existing `IConnectionMultiplexer` Redis instance — no new Redis connection.
- OpenTelemetry uses console exporter for dev; OTLP config key left empty by default for prod wiring.
- `GET /v1/orders/{id}/items` is a separate endpoint as required — does not remove items from `GET /v1/orders/{id}`.
