# Gruuber API Testing Guide

## Prerequisites

Start all infrastructure before running the API:

```bash
docker-compose up -d
```

Then start the API:

```bash
cd src/Gruuber.Api
dotnet run
```

---

## Swagger UI

Open in your browser: **http://localhost:5122/swagger**

> HTTPS alternative: https://localhost:7272/swagger

---

## Dev Seed Data

When running in `Development` mode, the API automatically seeds test data on startup. You will see `[DevSeeder]` log lines confirming this.

### Test Accounts (all use password `Password123!`)

| Role | Email | User ID |
|---|---|---|
| Rider | `rider@test.com` | `aaaaaaaa-0000-0000-0000-000000000001` |
| Driver | `driver@test.com` | `aaaaaaaa-0000-0000-0000-000000000002` |
| Restaurant | `restaurant@test.com` | `aaaaaaaa-0000-0000-0000-000000000003` |

### Other Seeded IDs

| Resource | ID |
|---|---|
| Restaurant | `bbbbbbbb-0000-0000-0000-000000000001` |
| Driver Redis location | Times Square, NYC — lat `40.7580`, lng `-73.9855`, region `1` |

---

## Authentication

All endpoints (except `/v1/auth/*`) require a JWT. Follow these steps:

### 1. Login

**`POST /v1/auth/login`**

```json
{
  "email": "rider@test.com",
  "password": "Password123!"
}
```

Copy the `accessToken` from the response.

### 2. Authorize in Swagger

Click the **Authorize 🔒** button at the top of the Swagger UI and enter:

```
Bearer <your-access-token>
```

### 3. Refresh a token

**`POST /v1/auth/refresh`**

```json
{
  "refreshToken": "<refresh-token-from-login>"
}
```

---

## Test Cases

Run these in order for a complete end-to-end flow.

### 1. Driver reports location

> Seed data does this automatically, but you can also call it manually.

**`POST /v1/drivers/location`**

```json
{
  "driverId": "aaaaaaaa-0000-0000-0000-000000000002",
  "lat": 40.7580,
  "lng": -73.9855,
  "regionId": 1,
  "activeRideId": null
}
```

**Expected:** `200 OK`

---

### 2. Request a ride

**`POST /v1/rides/request`**

```json
{
  "riderId": "aaaaaaaa-0000-0000-0000-000000000001",
  "rideType": "standard",
  "pickupLat": 40.7128,
  "pickupLng": -74.0060,
  "regionId": 1
}
```

**Expected:** `202 Accepted` with a `rideId`. Save it for the next steps.

---

### 3. Poll ride status

**`GET /v1/rides/{rideId}`**

**Expected:** `200 OK` — status will be `requested` or `matched`.

---

### 4. Create a food order

**`POST /v1/orders/create`**

```json
{
  "riderId": "aaaaaaaa-0000-0000-0000-000000000001",
  "restaurantId": "bbbbbbbb-0000-0000-0000-000000000001",
  "rideId": "00000000-0000-0000-0000-000000000000",
  "regionId": 1,
  "items": [
    {
      "menuItemId": "cccccccc-0000-0000-0000-000000000001",
      "quantity": 2,
      "price": 12.99
    }
  ]
}
```

**Expected:** `202 Accepted` with an `orderId`. Save it for the next steps.

---

### 5. Transition order status

**`PATCH /v1/orders/{orderId}/status`**

```json
{
  "newStatus": "accepted",
  "expectedVersion": 1,
  "regionId": 1
}
```

**Expected:** `200 OK`

#### Test optimistic concurrency (conflict scenario)

Re-send the same request with `"expectedVersion": 1` after a successful transition.

**Expected:** `409 Conflict`

```json
{
  "errorCode": "RESOURCE_CONFLICTED"
}
```

---

### 6. Initiate payment

**`POST /v1/payments`**

```json
{
  "rideId": "<your-ride-id>",
  "riderId": "aaaaaaaa-0000-0000-0000-000000000001",
  "amount": 25.50,
  "currency": "USD",
  "regionId": 1
}
```

**Expected:** `202 Accepted` with a `paymentId`. Save it.

---

### 7. Confirm payment

**`POST /v1/payments/{paymentId}/confirm`**

```json
{
  "expectedVersion": 1,
  "regionId": 1
}
```

**Expected:** `200 OK`

---

### 8. Fail a payment

**`POST /v1/payments/{paymentId}/fail`**

```json
{
  "expectedVersion": 1,
  "reason": "Insufficient funds",
  "regionId": 1
}
```

**Expected:** `200 OK`

---

## Health Checks

| Endpoint | Purpose |
|---|---|
| `GET /health` | Liveness — always returns `200` if the process is up |
| `GET /health/readiness` | Readiness — returns `503` if Redis or Postgres is unreachable |

---

## Automated Unit Tests

### Test Framework — MSTest

Design-pattern unit tests live in `tests/Gruuber.Tests/Unit/Patterns/` and use **MSTest**
(`Microsoft.VisualStudio.TestTools.UnitTesting`). All tests follow the **Arrange → Act → Assert** structure.

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gruuber.Tests.Unit.Patterns;

[TestClass] // Marks the class so the test runner discovers it
public class ExampleTests
{
    [TestMethod] // Marks the method as an executable test case
    public void MethodName_Scenario_ExpectedOutcome()
    {
        // 1. Arrange — set up objects and state
        var builder = new RideBuilder().ForRider(Guid.NewGuid()).InRegion(1).FromPickup(0, 0);

        // 2. Act — exercise the code under test
        var ride = builder.Build();

        // 3. Assert — verify the outcome
        Assert.AreEqual(RideStatus.Requested, ride.Status);
    }
}
```

### Running the pattern tests

```bash
# Run only design-pattern tests
dotnet test tests/Gruuber.Tests --filter "FullyQualifiedName~Gruuber.Tests.Unit.Patterns"

# Run all tests
dotnet test tests/Gruuber.Tests
```

### Test coverage per pattern

| File | Pattern | Tests |
|---|---|---|
| [`ResultStaticFactoryTests.cs`](../tests/Gruuber.Tests/Unit/Patterns/ResultStaticFactoryTests.cs) | Static Factory Method | `Result<T>.Ok/Fail`, `ApplicationResult<T>` factories (200, 202, 400, 409, 500) |
| [`BuilderTests.cs`](../tests/Gruuber.Tests/Unit/Patterns/BuilderTests.cs) | Builder | `RideBuilder` (solo, pool, fare, guards), `OrderBuilder` (items, totals, guards) |
| [`AbstractFactoryTests.cs`](../tests/Gruuber.Tests/Unit/Patterns/AbstractFactoryTests.cs) | Abstract Factory | `RideOutboxFactory` and `OrderOutboxFactory` — event type keys, JSON payloads |
| [`StateMachineTests.cs`](../tests/Gruuber.Tests/Unit/Patterns/StateMachineTests.cs) | State | Ride and Order state objects — legal/illegal transitions, terminal states, factory round-trip |
| [`SpecificationTests.cs`](../tests/Gruuber.Tests/Unit/Patterns/SpecificationTests.cs) | Specification | `And/Or/Not` composites, `DriverMatchEligibilitySpecification`, `OrderEligibilitySpecification` |
| [`MementoTests.cs`](../tests/Gruuber.Tests/Unit/Patterns/MementoTests.cs) | Memento | `Ride.CaptureSnapshot` / `RestoreFromSnapshot`, `Order` equivalents, independence of snapshots |
| [`UnitOfWorkTests.cs`](../tests/Gruuber.Tests/Unit/Patterns/UnitOfWorkTests.cs) | Unit of Work | `RidesUnitOfWork` and `OrdersUnitOfWork` — atomic commits, typed DbSet access, guards |
| [`PipelineBehaviorTests.cs`](../tests/Gruuber.Tests/Unit/Patterns/PipelineBehaviorTests.cs) | Decorator (MediatR) | `LoggingBehavior`, `ValidationBehavior`, `ErrorHandlingBehavior`, chained pipeline |
| [`MultitonTests.cs`](../tests/Gruuber.Tests/Unit/Patterns/MultitonTests.cs) | Multiton | `RegionedRedisDatabaseRegistry`, `RegionedKafkaProducerRegistry` — key isolation, `All` dictionary |

### MSTest assertion reference

| Assertion | Purpose |
|---|---|
| `Assert.AreEqual(expected, actual)` | Value equality |
| `Assert.AreSame(expected, actual)` | Reference equality (same object instance) |
| `Assert.IsTrue(condition)` / `Assert.IsFalse(condition)` | Boolean checks |
| `Assert.IsNull(obj)` / `Assert.IsNotNull(obj)` | Null checks |
| `Assert.IsInstanceOfType(obj, typeof(T))` | Type checks |
| `Assert.ThrowsExceptionAsync<T>(action)` | Async exception assertion |
| `[ExpectedException(typeof(T))]` attribute | Synchronous exception assertion |
| `Assert.AreEqual(expected, actual, delta)` | Floating-point equality with tolerance |
