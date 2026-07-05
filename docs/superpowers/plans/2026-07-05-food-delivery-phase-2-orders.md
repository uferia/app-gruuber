# Food Delivery Phase 2 — Order Rework & Payment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rework order creation to server-side pricing from menu data (no `RideId`, no client prices), add restaurant accept/reject/ready with role-guarded transitions and cancellation reasons, and initiate payment at placement with `CardMock` / `CashOnDelivery`.

**Architecture:** Orders consumes two SharedKernel seams: the existing `IRestaurantCatalogReader` (extended with `OwnerUserId` + `IsApproved`) for validation/pricing/ownership, and a new `IOrderPaymentInitiator` (implemented in Payments) for pay-at-placement — mirroring the `ISurgePricingService` precedent. Order lifecycle events flow through the existing `OrderOutbox` and adopt spec §6 names (`order_placed`, `order_accepted`, …, `order_delivered`, `order_cancelled`) which the Analytics consumer already expects but nothing publishes today.

**Tech Stack:** ASP.NET Core 8, EF Core 8 + Npgsql 8, plain handler classes, MSTest + FluentAssertions + Moq, EF InMemory (with `TransactionIgnoredWarning` suppressed — repo convention for transaction-using handlers).

**Spec:** `docs/superpowers/specs/2026-07-04-food-delivery-vertical-design.md` §5 (+ §3 architecture)

## Global Constraints

- All endpoints `/v1/...`; responses via `ApplicationResult<T>` + `result.ToHttpResult(this)`; 409 uses `ApplicationResult<T>.Conflict(...)` → error code `RESOURCE_CONFLICTED`.
- `RegionId`, `UserId`, and `Role` always from `ICurrentUserContext` (JWT claims: roles are `rider` | `driver` | `restaurant` | `admin`), never from request bodies.
- Order creation request carries NO `RideId` and NO prices: `RestaurantId`, `Items[] (MenuItemId, Quantity)`, `DeliveryLat/Lng`, `PaymentMethod`. Total = `(Σ menu prices + flat DeliveryFee from config "Orders:DeliveryFee", default 2.50m) × food-surge multiplier` via existing `ISurgePricingService` with rideType `"food"`.
- Transition role matrix (spec §5): `Placed→Accepted`, `Accepted→Preparing`, `Preparing→Ready` = restaurant owner of that order's restaurant; `Ready→PickedUp`, `PickedUp→Delivered` = assigned driver (`Order.DriverId == actor`); `Placed→Cancelled` = customer (order's rider), restaurant owner, admin, or system; `Ready→Cancelled` = system only. All other transitions are illegal → 400 `INVALID_TRANSITION`.
- Cancellation requires a `Reason` valid for the actor: restaurant = `ItemUnavailable, TooBusy, ClosingSoon, MenuPriceIncorrect, CannotFulfillInstructions, TechnicalIssue`; customer(rider) = `OrderedByMistake, WrongAddress, TakingTooLong, PaymentIssue, DuplicateOrder`; system = `RestaurantUnresponsive, NoDriverAvailable, PaymentFailed`; admin = any. Note max 500 chars.
- `PaymentMethod` enum values exactly `CardMock`, `CashOnDelivery`, defined once in SharedKernel; card-paid cancellations publish `payment_refund_requested` through the OrderOutbox; COD moves no money (driver confirms via existing `POST /v1/payments/{id}/confirm` at delivery — no new machinery, per spec "existing confirm machinery applies").
- Do NOT break existing code that this plan doesn't rewrite: keep `Order.Create(riderId, restaurantId, rideId, regionId)` (used by `OrderBuilder` + `OrderFareTests`); `Order.RideId` and `OrderSnapshot.RideId` become `Guid?`.
- Tests: MSTest + FluentAssertions + Moq, EF InMemory via `UseInMemoryDatabase(Guid.NewGuid().ToString())` with `.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` when the code under test opens transactions. No namespace declarations in test files. TDD per task.
- CancellationToken forwarded through every async call.

## File Structure

```
src/Gruuber.SharedKernel/
  Payments/PaymentContracts.cs              # NEW: PaymentMethod, IOrderPaymentInitiator, records
  Catalog/IRestaurantCatalogReader.cs       # MODIFY: CatalogRestaurant gains OwnerUserId, IsApproved (bool) replaces ApprovalStatus (string)

src/Gruuber.Restaurants/
  Infrastructure/RestaurantCatalogReader.cs # MODIFY: projection for new record shape

src/Gruuber.Payments/
  Domain/Payment.cs                         # MODIFY: RideId → Guid?, + OrderId, Method, CreateForOrder
  Application/Commands/PaymentCommands.cs   # MODIFY: PaymentDetailResponse gains OrderId/Method, RideId nullable
  Application/Commands/GetPaymentHandler.cs # MODIFY: mapping
  Application/OrderPaymentInitiator.cs      # NEW: IOrderPaymentInitiator implementation
  Infrastructure/PaymentsDbContext.cs       # MODIFY: Method conversion, OrderId index
  Migrations/ (generated)                   # AddOrderPaymentColumns
  PaymentsModule.cs                         # MODIFY: register initiator

src/Gruuber.Orders/
  Domain/OrderCancellationReason.cs         # NEW: enum + CancellationPolicy
  Domain/Order.cs                           # MODIFY: RideId Guid?, delivery/payment/cancellation fields, CreateForDelivery, TryCancel, SetDeliveryFee
  Domain/OrderSnapshot.cs                   # MODIFY: RideId → Guid?
  Application/OrderPricingOptions.cs        # NEW: record OrderPricingOptions(decimal DeliveryFee)
  Application/Commands/OrderCommands.cs     # MODIFY: reworked command records
  Application/Commands/CreateOrderHandler.cs    # REWRITE
  Application/Commands/TransitionOrderHandler.cs # REWRITE
  Application/Queries/OrderQueries.cs       # MODIFY: + restaurant-orders records
  Application/Queries/GetRestaurantOrdersHandler.cs # NEW
  Infrastructure/OrdersDbContext.cs         # MODIFY: new columns config
  Infrastructure/Migrations/ (generated)    # AddDeliveryOrderColumns
  OrdersModule.cs                           # MODIFY: options + new handler

src/Gruuber.Api/Controllers/
  OrdersController.cs                       # MODIFY: reworked create/transition requests
  RestaurantsController.cs                  # MODIFY: + GET mine/orders

tests/Gruuber.Tests/Unit/
  Restaurants/RestaurantCatalogReaderTests.cs  # MODIFY (Task 1)
  Payments/OrderPaymentInitiatorTests.cs       # NEW (Task 2)
  Orders/OrderDomainTests.cs                   # NEW (Task 3)
  Orders/CreateOrderHandlerTests.cs            # NEW (Task 4)
  Orders/TransitionOrderHandlerTests.cs        # NEW (Task 5)
  Orders/GetRestaurantOrdersHandlerTests.cs    # NEW (Task 6)
```

---

### Task 1: SharedKernel contracts — PaymentMethod, IOrderPaymentInitiator, extended CatalogRestaurant

**Files:**
- Create: `src/Gruuber.SharedKernel/Payments/PaymentContracts.cs`
- Modify: `src/Gruuber.SharedKernel/Catalog/IRestaurantCatalogReader.cs`
- Modify: `src/Gruuber.Restaurants/Infrastructure/RestaurantCatalogReader.cs`
- Test: `tests/Gruuber.Tests/Unit/Restaurants/RestaurantCatalogReaderTests.cs` (modify)

**Interfaces:**
- Consumes: `Restaurant` domain entity (`OwnerUserId`, `ApprovalStatus`, `IsOpen`, `RegionId`, `Lat`, `Lng`).
- Produces (later tasks rely on these verbatim):
  - `Gruuber.SharedKernel.Payments`: `enum PaymentMethod { CardMock, CashOnDelivery }`; `record OrderPaymentRequest(Guid OrderId, Guid RiderId, decimal Amount, string Currency, PaymentMethod Method, int RegionId)`; `record OrderPaymentResult(Guid PaymentId, string Status)`; `interface IOrderPaymentInitiator { Task<OrderPaymentResult> InitiateForOrderAsync(OrderPaymentRequest request, CancellationToken cancellationToken = default); }`
  - `Gruuber.SharedKernel.Catalog`: `record CatalogRestaurant(Guid Id, Guid OwnerUserId, string Name, bool IsApproved, bool IsOpen, int RegionId, double Lat, double Lng)` — **breaking change** to the Phase 1 record (zero consumers exist yet; final-review recommendation applied: typed `IsApproved` instead of a magic `"Approved"` string, `OwnerUserId` added for transition ownership guards). `CatalogMenuItem` unchanged.

- [ ] **Step 1: Update the catalog reader tests to the new record shape (failing first)**

In `tests/Gruuber.Tests/Unit/Restaurants/RestaurantCatalogReaderTests.cs`, replace the test `GetRestaurant_ReturnsStatusOpenAndLocation` with the two tests below (keep `CreateInMemoryDb`, `GetRestaurant_Unknown_ReturnsNull`, and `GetMenuItems_ReturnsOnlyRequestedIds` as they are):

```csharp
    [TestMethod]
    public async Task GetRestaurant_Approved_MapsOwnerStatusOpenAndLocation()
    {
        await using var db = CreateInMemoryDb();
        var ownerId = Guid.NewGuid();
        var restaurant = Restaurant.Create(ownerId, "Kanto Grill", "d", "Filipino", "a", 14.5, 120.9, 1);
        restaurant.Approve();
        restaurant.SetOpen(true);
        db.Restaurants.Add(restaurant);
        await db.SaveChangesAsync();
        var reader = new RestaurantCatalogReader(db);

        var result = await reader.GetRestaurantAsync(restaurant.Id);

        result.Should().NotBeNull();
        result!.OwnerUserId.Should().Be(ownerId);
        result.IsApproved.Should().BeTrue();
        result.IsOpen.Should().BeTrue();
        result.Lat.Should().Be(14.5);
        result.Lng.Should().Be(120.9);
        result.RegionId.Should().Be(1);
    }

    [TestMethod]
    public async Task GetRestaurant_Pending_IsApprovedFalse()
    {
        await using var db = CreateInMemoryDb();
        var restaurant = Restaurant.Create(Guid.NewGuid(), "Pending Place", "d", "Filipino", "a", 14.5, 120.9, 1);
        db.Restaurants.Add(restaurant);
        await db.SaveChangesAsync();
        var reader = new RestaurantCatalogReader(db);

        var result = await reader.GetRestaurantAsync(restaurant.Id);

        result!.IsApproved.Should().BeFalse();
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "FullyQualifiedName~RestaurantCatalogReaderTests"`
Expected: build FAILS — `CatalogRestaurant` has no `OwnerUserId`/`IsApproved`.

- [ ] **Step 3: Implement the contracts**

`src/Gruuber.SharedKernel/Payments/PaymentContracts.cs` (new):

```csharp
namespace Gruuber.SharedKernel.Payments;

public enum PaymentMethod
{
    CardMock,
    CashOnDelivery
}

public record OrderPaymentRequest(
    Guid OrderId,
    Guid RiderId,
    decimal Amount,
    string Currency,
    PaymentMethod Method,
    int RegionId);

public record OrderPaymentResult(Guid PaymentId, string Status);

public interface IOrderPaymentInitiator
{
    /// <summary>
    /// Creates a payment record for an order at placement time.
    /// Throws on persistence failure — callers treat any exception as payment-initiation failure.
    /// </summary>
    Task<OrderPaymentResult> InitiateForOrderAsync(OrderPaymentRequest request, CancellationToken cancellationToken = default);
}
```

Replace the records in `src/Gruuber.SharedKernel/Catalog/IRestaurantCatalogReader.cs` (interface methods unchanged):

```csharp
namespace Gruuber.SharedKernel.Catalog;

public record CatalogRestaurant(Guid Id, Guid OwnerUserId, string Name, bool IsApproved, bool IsOpen, int RegionId, double Lat, double Lng);
public record CatalogMenuItem(Guid Id, Guid RestaurantId, string Name, decimal Price, string Currency, bool IsAvailable);

public interface IRestaurantCatalogReader
{
    Task<CatalogRestaurant?> GetRestaurantAsync(Guid restaurantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogMenuItem>> GetMenuItemsAsync(IReadOnlyCollection<Guid> menuItemIds, CancellationToken cancellationToken = default);
}
```

In `src/Gruuber.Restaurants/Infrastructure/RestaurantCatalogReader.cs`, add `using Gruuber.Restaurants.Domain;` and replace the `GetRestaurantAsync` projection:

```csharp
    public async Task<CatalogRestaurant?> GetRestaurantAsync(Guid restaurantId, CancellationToken cancellationToken = default)
    {
        return await _db.Restaurants.AsNoTracking()
            .Where(r => r.Id == restaurantId)
            .Select(r => new CatalogRestaurant(
                r.Id, r.OwnerUserId, r.Name,
                r.ApprovalStatus == RestaurantApprovalStatus.Approved,
                r.IsOpen, r.RegionId, r.Lat, r.Lng))
            .FirstOrDefaultAsync(cancellationToken);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "FullyQualifiedName~RestaurantCatalogReaderTests"`
Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 5: Verify full build**

Run: `dotnet build src/Gruuber.Api/Gruuber.Api.csproj`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add src/Gruuber.SharedKernel src/Gruuber.Restaurants tests/Gruuber.Tests/Unit/Restaurants/RestaurantCatalogReaderTests.cs
git commit -m "feat(payments): SharedKernel payment contracts and typed catalog record"
```

---

### Task 2: Payments — order payments and IOrderPaymentInitiator

**Files:**
- Modify: `src/Gruuber.Payments/Domain/Payment.cs`
- Modify: `src/Gruuber.Payments/Application/Commands/PaymentCommands.cs` (PaymentDetailResponse)
- Modify: `src/Gruuber.Payments/Application/Commands/GetPaymentHandler.cs` (mapping)
- Create: `src/Gruuber.Payments/Application/OrderPaymentInitiator.cs`
- Modify: `src/Gruuber.Payments/Infrastructure/PaymentsDbContext.cs`
- Create: `src/Gruuber.Payments/Migrations/` → `AddOrderPaymentColumns` (generated; note existing migrations live under `Infrastructure/Migrations` — use `--output-dir Infrastructure/Migrations`)
- Modify: `src/Gruuber.Payments/PaymentsModule.cs`
- Test: `tests/Gruuber.Tests/Unit/Payments/OrderPaymentInitiatorTests.cs`

**Interfaces:**
- Consumes: `PaymentMethod`, `IOrderPaymentInitiator`, `OrderPaymentRequest`, `OrderPaymentResult` (Task 1); existing `Payment`, `PaymentOutboxEntry`, `PaymentsDbContext`.
- Produces: `Payment.CreateForOrder(Guid orderId, Guid riderId, decimal amount, string currency, PaymentMethod method)`; `Payment.RideId` becomes `Guid?`; new `Payment.OrderId` (`Guid?`) and `Payment.Method` (`PaymentMethod`, default `CardMock`); `OrderPaymentInitiator : IOrderPaymentInitiator` registered in DI. `PaymentDetailResponse` becomes `(Guid Id, Guid? RideId, Guid? OrderId, string Method, string Status, decimal Amount, string Currency, DateTime CreatedAt)`.

- [ ] **Step 1: Write the failing tests**

`tests/Gruuber.Tests/Unit/Payments/OrderPaymentInitiatorTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using Gruuber.Payments.Application;
using Gruuber.Payments.Domain;
using Gruuber.Payments.Infrastructure;
using Gruuber.SharedKernel.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class OrderPaymentInitiatorTests
{
    private static PaymentsDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new PaymentsDbContext(opts);
    }

    [TestMethod]
    public async Task Initiate_CreatesPaymentWithOrderIdAndMethod()
    {
        await using var db = CreateInMemoryDb();
        var initiator = new OrderPaymentInitiator(db, NullLogger<OrderPaymentInitiator>.Instance);
        var orderId = Guid.NewGuid();
        var riderId = Guid.NewGuid();

        var result = await initiator.InitiateForOrderAsync(
            new OrderPaymentRequest(orderId, riderId, 392.50m, "PHP", PaymentMethod.CashOnDelivery, 1));

        result.Status.Should().Be("Initiated");
        var payment = await db.Payments.SingleAsync();
        payment.Id.Should().Be(result.PaymentId);
        payment.OrderId.Should().Be(orderId);
        payment.RiderId.Should().Be(riderId);
        payment.RideId.Should().BeNull();
        payment.Method.Should().Be(PaymentMethod.CashOnDelivery);
        payment.Amount.Should().Be(392.50m);
        payment.Status.Should().Be(PaymentStatus.Initiated);
    }

    [TestMethod]
    public async Task Initiate_WritesPaymentInitiatedOutboxEvent()
    {
        await using var db = CreateInMemoryDb();
        var initiator = new OrderPaymentInitiator(db, NullLogger<OrderPaymentInitiator>.Instance);
        var orderId = Guid.NewGuid();

        await initiator.InitiateForOrderAsync(
            new OrderPaymentRequest(orderId, Guid.NewGuid(), 100m, "PHP", PaymentMethod.CardMock, 7));

        var outbox = await db.Set<PaymentOutboxEntry>().SingleAsync();
        outbox.EventType.Should().Be("payment-events-7");
        using var doc = JsonDocument.Parse(outbox.Payload);
        doc.RootElement.GetProperty("EventName").GetString().Should().Be("payment_initiated");
        doc.RootElement.GetProperty("OrderId").GetGuid().Should().Be(orderId);
        doc.RootElement.GetProperty("Method").GetString().Should().Be("CardMock");
    }

    [TestMethod]
    public void CreateForOrder_DefaultsInitiatedVersion1()
    {
        var payment = Payment.CreateForOrder(Guid.NewGuid(), Guid.NewGuid(), 50m, "USD", PaymentMethod.CardMock);

        payment.Status.Should().Be(PaymentStatus.Initiated);
        payment.Version.Should().Be(1);
        payment.RideId.Should().BeNull();
        payment.OrderId.Should().NotBeNull();
    }

    [TestMethod]
    public void Create_ForRide_StillWorks_MethodDefaultsCardMock()
    {
        var rideId = Guid.NewGuid();
        var payment = Payment.Create(rideId, Guid.NewGuid(), 25m, "USD");

        payment.RideId.Should().Be(rideId);
        payment.OrderId.Should().BeNull();
        payment.Method.Should().Be(PaymentMethod.CardMock);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "FullyQualifiedName~OrderPaymentInitiatorTests"`
Expected: build FAILS — `OrderPaymentInitiator`, `Payment.OrderId`, `CreateForOrder` do not exist.

- [ ] **Step 3: Implement**

In `src/Gruuber.Payments/Domain/Payment.cs`: add `using Gruuber.SharedKernel.Payments;`, change the `RideId` property, add `OrderId`/`Method`, and add the factory (existing `Create`, `TryConfirm`, `TryFail`, `TryTimeout`, `IncrementPollingAttempt` unchanged):

```csharp
    public Guid? RideId { get; private set; }
    public Guid? OrderId { get; private set; }
    public PaymentMethod Method { get; private set; } = PaymentMethod.CardMock;
```

```csharp
    public static Payment CreateForOrder(Guid orderId, Guid riderId, decimal amount, string currency, PaymentMethod method)
    {
        return new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            RiderId = riderId,
            Amount = amount,
            Currency = currency,
            Method = method,
            Status = PaymentStatus.Initiated,
            CreatedAt = DateTime.UtcNow,
            Version = 1
        };
    }
```

In `src/Gruuber.Payments/Application/Commands/PaymentCommands.cs`, replace `PaymentDetailResponse`:

```csharp
public record PaymentDetailResponse(Guid Id, Guid? RideId, Guid? OrderId, string Method, string Status, decimal Amount, string Currency, DateTime CreatedAt);
```

In `src/Gruuber.Payments/Application/Commands/GetPaymentHandler.cs`, replace the success mapping:

```csharp
        return ApplicationResult<PaymentDetailResponse>.Success(
            new PaymentDetailResponse(
                payment.Id,
                payment.RideId,
                payment.OrderId,
                payment.Method.ToString(),
                payment.Status.ToString(),
                payment.Amount,
                payment.Currency,
                payment.CreatedAt));
```

`src/Gruuber.Payments/Application/OrderPaymentInitiator.cs` (new):

```csharp
using System.Text.Json;
using Gruuber.Payments.Domain;
using Gruuber.Payments.Infrastructure;
using Gruuber.SharedKernel.Payments;
using Microsoft.Extensions.Logging;

namespace Gruuber.Payments.Application;

public class OrderPaymentInitiator : IOrderPaymentInitiator
{
    private readonly PaymentsDbContext _db;
    private readonly ILogger<OrderPaymentInitiator> _logger;

    public OrderPaymentInitiator(PaymentsDbContext db, ILogger<OrderPaymentInitiator> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<OrderPaymentResult> InitiateForOrderAsync(
        OrderPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        var payment = Payment.CreateForOrder(request.OrderId, request.RiderId, request.Amount, request.Currency, request.Method);

        var outbox = new PaymentOutboxEntry
        {
            EventType = $"payment-events-{request.RegionId}",
            Payload = JsonSerializer.Serialize(new
            {
                EventName = "payment_initiated",
                PaymentId = payment.Id,
                payment.OrderId,
                payment.RiderId,
                payment.Amount,
                payment.Currency,
                Method = payment.Method.ToString(),
                OccurredAt = DateTime.UtcNow
            })
        };

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.Payments.Add(payment);
        _db.Set<PaymentOutboxEntry>().Add(outbox);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Payment {PaymentId} initiated for order {OrderId} amount {Amount} {Currency} method {Method}",
            payment.Id, payment.OrderId, payment.Amount, payment.Currency, payment.Method);

        return new OrderPaymentResult(payment.Id, payment.Status.ToString());
    }
}
```

In `src/Gruuber.Payments/Infrastructure/PaymentsDbContext.cs`, add inside the `Payment` entity block (after the `Version` line):

```csharp
            e.Property(x => x.Method).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => x.OrderId);
```

In `src/Gruuber.Payments/PaymentsModule.cs`, add `using Gruuber.SharedKernel.Payments;` and register:

```csharp
        services.AddScoped<IOrderPaymentInitiator, OrderPaymentInitiator>();
```

- [ ] **Step 4: Generate the migration**

Run: `dotnet ef migrations add AddOrderPaymentColumns --project src/Gruuber.Payments --output-dir Infrastructure/Migrations`
Expected: `Done.` — migration alters `RideId` to nullable and adds `OrderId` (nullable uuid), `Method` (text/varchar), plus the `OrderId` index.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "FullyQualifiedName~OrderPaymentInitiatorTests"`
Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 6: Verify full build**

Run: `dotnet build src/Gruuber.Api/Gruuber.Api.csproj`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 7: Commit**

```bash
git add src/Gruuber.Payments tests/Gruuber.Tests/Unit/Payments
git commit -m "feat(payments): order payments with PaymentMethod and IOrderPaymentInitiator"
```

---

### Task 3: Orders domain — delivery fields, cancellation reasons, TryCancel

**Files:**
- Create: `src/Gruuber.Orders/Domain/OrderCancellationReason.cs`
- Modify: `src/Gruuber.Orders/Domain/Order.cs`
- Modify: `src/Gruuber.Orders/Domain/OrderSnapshot.cs` (`RideId` → `Guid?`)
- Modify: `src/Gruuber.Orders/Infrastructure/OrdersDbContext.cs`
- Create: `src/Gruuber.Orders/Infrastructure/Migrations/` → `AddDeliveryOrderColumns` (generated)
- Test: `tests/Gruuber.Tests/Unit/Orders/OrderDomainTests.cs`

**Interfaces:**
- Consumes: `PaymentMethod` (Task 1), existing `Order`/`OrderStatus`/`OrderItem`.
- Produces:
  - `enum OrderCancellationReason { ItemUnavailable, TooBusy, ClosingSoon, MenuPriceIncorrect, CannotFulfillInstructions, TechnicalIssue, OrderedByMistake, WrongAddress, TakingTooLong, PaymentIssue, DuplicateOrder, RestaurantUnresponsive, NoDriverAvailable, PaymentFailed }`
  - `static class CancellationPolicy { static bool IsAllowed(OrderCancellationReason reason, string actorRole); }` — roles `restaurant`/`rider`/`system`/`admin` per Global Constraints.
  - On `Order`: `Guid? RideId` (was `Guid`); new `double DeliveryLat`, `double DeliveryLng`, `decimal DeliveryFee`, `PaymentMethod PaymentMethod` (default `CardMock`), `OrderCancellationReason? CancellationReason`, `string? CancellationNote`, `string? CancelledByRole`; `static Order CreateForDelivery(Guid riderId, Guid restaurantId, int regionId, double deliveryLat, double deliveryLng, PaymentMethod paymentMethod)`; `void SetDeliveryFee(decimal fee)`; `bool TryCancel(OrderCancellationReason reason, string? note, string cancelledByRole, long expectedVersion)`.
  - Existing `Order.Create(riderId, restaurantId, rideId, regionId)`, `AddItem`, `TryTransition`, `TryAssignDriver`, `ApplySurge` unchanged.

- [ ] **Step 1: Write the failing tests**

`tests/Gruuber.Tests/Unit/Orders/OrderDomainTests.cs`:

```csharp
using FluentAssertions;
using Gruuber.Orders.Domain;
using Gruuber.SharedKernel.Payments;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class OrderDomainTests
{
    private static Order NewDeliveryOrder(PaymentMethod method = PaymentMethod.CardMock) =>
        Order.CreateForDelivery(Guid.NewGuid(), Guid.NewGuid(), 1, 14.60, 120.98, method);

    [TestMethod]
    public void CreateForDelivery_StartsPlacedNoRideVersion1()
    {
        var order = NewDeliveryOrder(PaymentMethod.CashOnDelivery);

        order.Status.Should().Be(OrderStatus.Placed);
        order.RideId.Should().BeNull();
        order.DriverId.Should().BeNull();
        order.PaymentMethod.Should().Be(PaymentMethod.CashOnDelivery);
        order.DeliveryLat.Should().Be(14.60);
        order.DeliveryLng.Should().Be(120.98);
        order.Version.Should().Be(1);
        order.CancellationReason.Should().BeNull();
    }

    [TestMethod]
    public void SetDeliveryFee_StoresFee()
    {
        var order = NewDeliveryOrder();

        order.SetDeliveryFee(2.50m);

        order.DeliveryFee.Should().Be(2.50m);
    }

    [TestMethod]
    public void TryCancel_WithCorrectVersion_SetsCancellationFieldsAndBumpsVersion()
    {
        var order = NewDeliveryOrder();

        var ok = order.TryCancel(OrderCancellationReason.TooBusy, "Kitchen slammed", "restaurant", 1);

        ok.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be(OrderCancellationReason.TooBusy);
        order.CancellationNote.Should().Be("Kitchen slammed");
        order.CancelledByRole.Should().Be("restaurant");
        order.Version.Should().Be(2);
    }

    [TestMethod]
    public void TryCancel_WithStaleVersion_ReturnsFalseAndChangesNothing()
    {
        var order = NewDeliveryOrder();

        var ok = order.TryCancel(OrderCancellationReason.TooBusy, null, "restaurant", 99);

        ok.Should().BeFalse();
        order.Status.Should().Be(OrderStatus.Placed);
        order.CancellationReason.Should().BeNull();
        order.Version.Should().Be(1);
    }

    [TestMethod]
    public void CancellationPolicy_RestaurantReasons()
    {
        CancellationPolicy.IsAllowed(OrderCancellationReason.ItemUnavailable, "restaurant").Should().BeTrue();
        CancellationPolicy.IsAllowed(OrderCancellationReason.TechnicalIssue, "restaurant").Should().BeTrue();
        CancellationPolicy.IsAllowed(OrderCancellationReason.OrderedByMistake, "restaurant").Should().BeFalse();
        CancellationPolicy.IsAllowed(OrderCancellationReason.NoDriverAvailable, "restaurant").Should().BeFalse();
    }

    [TestMethod]
    public void CancellationPolicy_CustomerReasons()
    {
        CancellationPolicy.IsAllowed(OrderCancellationReason.OrderedByMistake, "rider").Should().BeTrue();
        CancellationPolicy.IsAllowed(OrderCancellationReason.DuplicateOrder, "rider").Should().BeTrue();
        CancellationPolicy.IsAllowed(OrderCancellationReason.TooBusy, "rider").Should().BeFalse();
        CancellationPolicy.IsAllowed(OrderCancellationReason.PaymentFailed, "rider").Should().BeFalse();
    }

    [TestMethod]
    public void CancellationPolicy_SystemAndAdmin()
    {
        CancellationPolicy.IsAllowed(OrderCancellationReason.RestaurantUnresponsive, "system").Should().BeTrue();
        CancellationPolicy.IsAllowed(OrderCancellationReason.NoDriverAvailable, "system").Should().BeTrue();
        CancellationPolicy.IsAllowed(OrderCancellationReason.PaymentFailed, "system").Should().BeTrue();
        CancellationPolicy.IsAllowed(OrderCancellationReason.ItemUnavailable, "system").Should().BeFalse();
        CancellationPolicy.IsAllowed(OrderCancellationReason.ItemUnavailable, "admin").Should().BeTrue();
        CancellationPolicy.IsAllowed(OrderCancellationReason.NoDriverAvailable, "admin").Should().BeTrue();
        CancellationPolicy.IsAllowed(OrderCancellationReason.ItemUnavailable, "driver").Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "FullyQualifiedName~OrderDomainTests"`
Expected: build FAILS — `CreateForDelivery`, `OrderCancellationReason`, `CancellationPolicy` do not exist.

- [ ] **Step 3: Implement**

`src/Gruuber.Orders/Domain/OrderCancellationReason.cs` (new):

```csharp
namespace Gruuber.Orders.Domain;

public enum OrderCancellationReason
{
    // Restaurant reject reasons
    ItemUnavailable,
    TooBusy,
    ClosingSoon,
    MenuPriceIncorrect,
    CannotFulfillInstructions,
    TechnicalIssue,
    // Customer cancel reasons
    OrderedByMistake,
    WrongAddress,
    TakingTooLong,
    PaymentIssue,
    DuplicateOrder,
    // System reasons
    RestaurantUnresponsive,
    NoDriverAvailable,
    PaymentFailed
}

public static class CancellationPolicy
{
    private static readonly OrderCancellationReason[] RestaurantReasons =
    {
        OrderCancellationReason.ItemUnavailable,
        OrderCancellationReason.TooBusy,
        OrderCancellationReason.ClosingSoon,
        OrderCancellationReason.MenuPriceIncorrect,
        OrderCancellationReason.CannotFulfillInstructions,
        OrderCancellationReason.TechnicalIssue
    };

    private static readonly OrderCancellationReason[] CustomerReasons =
    {
        OrderCancellationReason.OrderedByMistake,
        OrderCancellationReason.WrongAddress,
        OrderCancellationReason.TakingTooLong,
        OrderCancellationReason.PaymentIssue,
        OrderCancellationReason.DuplicateOrder
    };

    private static readonly OrderCancellationReason[] SystemReasons =
    {
        OrderCancellationReason.RestaurantUnresponsive,
        OrderCancellationReason.NoDriverAvailable,
        OrderCancellationReason.PaymentFailed
    };

    public static bool IsAllowed(OrderCancellationReason reason, string actorRole) => actorRole switch
    {
        "restaurant" => RestaurantReasons.Contains(reason),
        "rider" => CustomerReasons.Contains(reason),
        "system" => SystemReasons.Contains(reason),
        "admin" => true,
        _ => false
    };
}
```

In `src/Gruuber.Orders/Domain/Order.cs`: add `using Gruuber.SharedKernel.Payments;`; change `public Guid RideId { get; private set; }` to `public Guid? RideId { get; private set; }`; add the new properties after `SurgeReason`:

```csharp
    public double DeliveryLat { get; private set; }
    public double DeliveryLng { get; private set; }
    public decimal DeliveryFee { get; private set; }
    public PaymentMethod PaymentMethod { get; private set; } = PaymentMethod.CardMock;
    public OrderCancellationReason? CancellationReason { get; private set; }
    public string? CancellationNote { get; private set; }
    public string? CancelledByRole { get; private set; }
```

and add after the existing `Create`:

```csharp
    public static Order CreateForDelivery(
        Guid riderId,
        Guid restaurantId,
        int regionId,
        double deliveryLat,
        double deliveryLng,
        PaymentMethod paymentMethod)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            RiderId = riderId,
            RestaurantId = restaurantId,
            RideId = null,
            Status = OrderStatus.Placed,
            RegionId = regionId,
            DeliveryLat = deliveryLat,
            DeliveryLng = deliveryLng,
            PaymentMethod = paymentMethod,
            CreatedAt = DateTime.UtcNow,
            Version = 1
        };
    }

    public void SetDeliveryFee(decimal fee) => DeliveryFee = fee;

    public bool TryCancel(OrderCancellationReason reason, string? note, string cancelledByRole, long expectedVersion)
    {
        if (Version != expectedVersion)
            return false;

        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        CancellationNote = note;
        CancelledByRole = cancelledByRole;
        Version++;
        return true;
    }
```

In `src/Gruuber.Orders/Domain/OrderSnapshot.cs`, change `public Guid RideId { get; init; }` to `public Guid? RideId { get; init; }` (the memento `CaptureSnapshot` assignment then compiles unchanged; `MementoTests` assertion `snapshot.RideId.Should().Be(rideId)` still passes against a `Guid?`).

In `src/Gruuber.Orders/Infrastructure/OrdersDbContext.cs`, add `using Gruuber.SharedKernel.Payments;` and add inside the `Order` entity block (after the `SurgeReason` line):

```csharp
            e.Property(x => x.DeliveryFee).HasColumnType("numeric(10,2)").HasDefaultValue(0m);
            e.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(32).HasDefaultValue(PaymentMethod.CardMock);
            e.Property(x => x.CancellationReason).HasConversion<string>().HasMaxLength(64);
            e.Property(x => x.CancellationNote).HasMaxLength(500);
            e.Property(x => x.CancelledByRole).HasMaxLength(32);
            e.HasIndex(x => new { x.RestaurantId, x.Status });
```

- [ ] **Step 4: Generate the migration**

Run: `dotnet ef migrations add AddDeliveryOrderColumns --project src/Gruuber.Orders --output-dir Infrastructure/Migrations`
Expected: `Done.` — `RideId` altered to nullable; `DeliveryLat`, `DeliveryLng`, `DeliveryFee`, `PaymentMethod`, `CancellationReason`, `CancellationNote`, `CancelledByRole` columns and the `(RestaurantId, Status)` index added.

- [ ] **Step 5: Run tests to verify they pass (plus the pre-existing suites that touch Order)**

Run: `dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "FullyQualifiedName~OrderDomainTests|FullyQualifiedName~OrderFareTests|FullyQualifiedName~MementoTests|FullyQualifiedName~BuilderTests"`
Expected: all pass, 0 failed (7 new + the existing fare/memento/builder tests).

- [ ] **Step 6: Commit**

```bash
git add src/Gruuber.Orders tests/Gruuber.Tests/Unit/Orders
git commit -m "feat(orders): delivery order domain with cancellation reasons"
```

---

### Task 4: CreateOrderHandler rework — server-side pricing and pay-at-placement

**Files:**
- Modify: `src/Gruuber.Orders/Application/Commands/OrderCommands.cs`
- Rewrite: `src/Gruuber.Orders/Application/Commands/CreateOrderHandler.cs`
- Create: `src/Gruuber.Orders/Application/OrderPricingOptions.cs`
- Modify: `src/Gruuber.Orders/OrdersModule.cs`
- Modify: `src/Gruuber.Api/Controllers/OrdersController.cs` (create endpoint + request records)
- Test: `tests/Gruuber.Tests/Unit/Orders/CreateOrderHandlerTests.cs`

**Interfaces:**
- Consumes: `IRestaurantCatalogReader` + `CatalogRestaurant`/`CatalogMenuItem` (Task 1), `IOrderPaymentInitiator`/`OrderPaymentRequest`/`OrderPaymentResult`/`PaymentMethod` (Tasks 1–2), `Order.CreateForDelivery`/`SetDeliveryFee`/`TryCancel` (Task 3), existing `ISurgePricingService` (`ResolveAsync(regionId, "food", baseFare, ct)` → `SurgeResolution(Multiplier, Reason, BaseFare, FinalFare)`), `FareEstimate(BaseFare, FinalFare, SurgeMultiplier?, SurgeReason?)`.
- Produces:
  - `CreateOrderCommand(Guid RiderId, Guid RestaurantId, int RegionId, IList<OrderItemRequest> Items, double DeliveryLat, double DeliveryLng, PaymentMethod PaymentMethod)`
  - `OrderItemRequest(Guid MenuItemId, int Quantity)` — price removed
  - `CreateOrderResponse(Guid OrderId, string Status, Guid PaymentId, decimal Total, FareEstimate? Fare = null)`
  - `record OrderPricingOptions(decimal DeliveryFee)` registered as singleton from config key `Orders:DeliveryFee` (default `2.50m`)
  - Error codes: `RESTAURANT_NOT_FOUND` 404, `RESTAURANT_UNAVAILABLE` 400, `REGION_MISMATCH` 400, `INVALID_MENU_ITEM` 400, `ITEM_UNAVAILABLE` 400, `PAYMENT_FAILED` 400 (order auto-cancelled with system reason `PaymentFailed`).
  - Outbox event on placement: `order_placed` on topic `order-events-{RegionId}`.

- [ ] **Step 1: Write the failing tests**

`tests/Gruuber.Tests/Unit/Orders/CreateOrderHandlerTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using Gruuber.Orders.Application;
using Gruuber.Orders.Application.Commands;
using Gruuber.Orders.Domain;
using Gruuber.Orders.Infrastructure;
using Gruuber.SharedKernel.Catalog;
using Gruuber.SharedKernel.Payments;
using Gruuber.SharedKernel.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class CreateOrderHandlerTests
{
    private static readonly Guid RestaurantId = Guid.NewGuid();
    private static readonly Guid ItemA = Guid.NewGuid();
    private static readonly Guid ItemB = Guid.NewGuid();

    private static OrdersDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new OrdersDbContext(opts);
    }

    private static CatalogRestaurant OpenRestaurant(int regionId = 1) =>
        new(RestaurantId, Guid.NewGuid(), "Kanto Grill", IsApproved: true, IsOpen: true, regionId, 14.5, 120.9);

    private static Mock<IRestaurantCatalogReader> CatalogWith(CatalogRestaurant? restaurant, params CatalogMenuItem[] items)
    {
        var catalog = new Mock<IRestaurantCatalogReader>();
        catalog.Setup(c => c.GetRestaurantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(restaurant);
        catalog.Setup(c => c.GetMenuItemsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(items.ToList());
        return catalog;
    }

    private static Mock<ISurgePricingService> NoSurge()
    {
        var surge = new Mock<ISurgePricingService>();
        surge.Setup(s => s.ResolveAsync(It.IsAny<int>(), "food", It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, string _, decimal baseFare, CancellationToken _) =>
                new SurgeResolution(1.0m, null, baseFare, baseFare));
        return surge;
    }

    private static Mock<IOrderPaymentInitiator> PaymentsOk()
    {
        var payments = new Mock<IOrderPaymentInitiator>();
        payments.Setup(p => p.InitiateForOrderAsync(It.IsAny<OrderPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderPaymentResult(Guid.NewGuid(), "Initiated"));
        return payments;
    }

    private static CreateOrderHandler Handler(
        OrdersDbContext db,
        Mock<IRestaurantCatalogReader> catalog,
        Mock<IOrderPaymentInitiator>? payments = null) =>
        new(db, catalog.Object, NoSurge().Object, (payments ?? PaymentsOk()).Object,
            new OrderPricingOptions(2.50m), NullLogger<CreateOrderHandler>.Instance);

    private static CreateOrderCommand Command(params OrderItemRequest[] items) =>
        new(Guid.NewGuid(), RestaurantId, 1, items.ToList(), 14.60, 120.98, PaymentMethod.CardMock);

    [TestMethod]
    public async Task Create_PricesFromMenuAndInitiatesPayment()
    {
        await using var db = CreateInMemoryDb();
        var catalog = CatalogWith(OpenRestaurant(),
            new CatalogMenuItem(ItemA, RestaurantId, "Pork BBQ", 120m, "PHP", true),
            new CatalogMenuItem(ItemB, RestaurantId, "Sisig", 150m, "PHP", true));
        var payments = PaymentsOk();
        var handler = Handler(db, catalog, payments);

        var result = await handler.HandleAsync(Command(new(ItemA, 2), new(ItemB, 1)));

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(202);
        // 2*120 + 1*150 = 390 subtotal; +2.50 delivery fee = 392.50; surge 1.0
        result.Data!.Total.Should().Be(392.50m);
        var order = await db.Orders.Include(o => o.Items).SingleAsync();
        order.TotalAmount.Should().Be(390m);
        order.DeliveryFee.Should().Be(2.50m);
        order.FinalFare.Should().Be(392.50m);
        order.RideId.Should().BeNull();
        order.Items.Should().HaveCount(2);
        order.Items.Single(i => i.MenuItemId == ItemA).Price.Should().Be(120m);
        payments.Verify(p => p.InitiateForOrderAsync(
            It.Is<OrderPaymentRequest>(r => r.OrderId == order.Id && r.Amount == 392.50m && r.Currency == "PHP" && r.Method == PaymentMethod.CardMock),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task Create_WritesOrderPlacedOutboxEvent()
    {
        await using var db = CreateInMemoryDb();
        var catalog = CatalogWith(OpenRestaurant(),
            new CatalogMenuItem(ItemA, RestaurantId, "Pork BBQ", 120m, "PHP", true));
        var handler = Handler(db, catalog);

        await handler.HandleAsync(Command(new(ItemA, 1)));

        var outbox = await db.Set<OrderOutboxEntry>().SingleAsync();
        outbox.EventType.Should().Be("order-events-1");
        using var doc = JsonDocument.Parse(outbox.Payload);
        doc.RootElement.GetProperty("EventName").GetString().Should().Be("order_placed");
    }

    [TestMethod]
    public async Task Create_UnknownRestaurant_Returns404()
    {
        await using var db = CreateInMemoryDb();
        var handler = Handler(db, CatalogWith(null));

        var result = await handler.HandleAsync(Command(new(ItemA, 1)));

        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("RESTAURANT_NOT_FOUND");
    }

    [TestMethod]
    public async Task Create_ClosedRestaurant_Returns400()
    {
        await using var db = CreateInMemoryDb();
        var closed = new CatalogRestaurant(RestaurantId, Guid.NewGuid(), "Kanto Grill", true, IsOpen: false, 1, 14.5, 120.9);
        var handler = Handler(db, CatalogWith(closed,
            new CatalogMenuItem(ItemA, RestaurantId, "Pork BBQ", 120m, "PHP", true)));

        var result = await handler.HandleAsync(Command(new(ItemA, 1)));

        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("RESTAURANT_UNAVAILABLE");
    }

    [TestMethod]
    public async Task Create_UnavailableItem_Returns400()
    {
        await using var db = CreateInMemoryDb();
        var handler = Handler(db, CatalogWith(OpenRestaurant(),
            new CatalogMenuItem(ItemA, RestaurantId, "Pork BBQ", 120m, "PHP", IsAvailable: false)));

        var result = await handler.HandleAsync(Command(new(ItemA, 1)));

        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("ITEM_UNAVAILABLE");
    }

    [TestMethod]
    public async Task Create_ItemFromAnotherRestaurant_Returns400()
    {
        await using var db = CreateInMemoryDb();
        var handler = Handler(db, CatalogWith(OpenRestaurant(),
            new CatalogMenuItem(ItemA, Guid.NewGuid(), "Foreign Item", 120m, "PHP", true)));

        var result = await handler.HandleAsync(Command(new(ItemA, 1)));

        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("INVALID_MENU_ITEM");
    }

    [TestMethod]
    public async Task Create_PaymentInitiationFails_CancelsOrderWithPaymentFailed()
    {
        await using var db = CreateInMemoryDb();
        var catalog = CatalogWith(OpenRestaurant(),
            new CatalogMenuItem(ItemA, RestaurantId, "Pork BBQ", 120m, "PHP", true));
        var payments = new Mock<IOrderPaymentInitiator>();
        payments.Setup(p => p.InitiateForOrderAsync(It.IsAny<OrderPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("payments store down"));
        var handler = Handler(db, catalog, payments);

        var result = await handler.HandleAsync(Command(new(ItemA, 1)));

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("PAYMENT_FAILED");
        var order = await db.Orders.SingleAsync();
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be(OrderCancellationReason.PaymentFailed);
        order.CancelledByRole.Should().Be("system");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "FullyQualifiedName~CreateOrderHandlerTests"`
Expected: build FAILS — new command shape/`OrderPricingOptions`/handler constructor don't exist.

- [ ] **Step 3: Implement**

`src/Gruuber.Orders/Application/OrderPricingOptions.cs` (new):

```csharp
namespace Gruuber.Orders.Application;

public record OrderPricingOptions(decimal DeliveryFee);
```

Replace `src/Gruuber.Orders/Application/Commands/OrderCommands.cs`:

```csharp
using Gruuber.SharedKernel.Payments;
using Gruuber.SharedKernel.Pricing;

namespace Gruuber.Orders.Application.Commands;

public record CreateOrderCommand(
    Guid RiderId,
    Guid RestaurantId,
    int RegionId,
    IList<OrderItemRequest> Items,
    double DeliveryLat,
    double DeliveryLng,
    PaymentMethod PaymentMethod);

public record OrderItemRequest(Guid MenuItemId, int Quantity);

public record CreateOrderResponse(Guid OrderId, string Status, Guid PaymentId, decimal Total, FareEstimate? Fare = null);

public record TransitionOrderCommand(
    Guid OrderId,
    string NewStatus,
    long ExpectedVersion,
    int RegionId,
    Guid ActorUserId,
    string ActorRole,
    string? Reason = null,
    string? Note = null);
```

(The extended `TransitionOrderCommand` lands here so the file changes once; Task 5 implements the handler that uses the new fields. `TransitionOrderHandler` won't compile against the new record until its call site is updated — Task 4 also updates the controller call for BOTH endpoints, and Task 5 rewrites the handler. To keep Task 4 buildable, ALSO apply the minimal handler-signature fix in this task: in `TransitionOrderHandler.HandleAsync` nothing references removed fields — the record only gained optional/new fields, and the existing positional construction site in the controller is updated below. No other change to `TransitionOrderHandler` in this task.)

Replace `src/Gruuber.Orders/Application/Commands/CreateOrderHandler.cs`:

```csharp
using System.Text.Json;
using Gruuber.Orders.Domain;
using Gruuber.Orders.Infrastructure;
using Gruuber.SharedKernel.Catalog;
using Gruuber.SharedKernel.Payments;
using Gruuber.SharedKernel.Pricing;
using Gruuber.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Gruuber.Orders.Application.Commands;

public class CreateOrderHandler
{
    private readonly OrdersDbContext _db;
    private readonly IRestaurantCatalogReader _catalog;
    private readonly ISurgePricingService _surge;
    private readonly IOrderPaymentInitiator _payments;
    private readonly OrderPricingOptions _pricing;
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(
        OrdersDbContext db,
        IRestaurantCatalogReader catalog,
        ISurgePricingService surge,
        IOrderPaymentInitiator payments,
        OrderPricingOptions pricing,
        ILogger<CreateOrderHandler> logger)
    {
        _db = db;
        _catalog = catalog;
        _surge = surge;
        _payments = payments;
        _pricing = pricing;
        _logger = logger;
    }

    public async Task<ApplicationResult<CreateOrderResponse>> HandleAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var restaurant = await _catalog.GetRestaurantAsync(command.RestaurantId, cancellationToken);
        if (restaurant is null)
            return ApplicationResult<CreateOrderResponse>.Failure("RESTAURANT_NOT_FOUND", "Restaurant not found.", 404);

        if (!restaurant.IsApproved || !restaurant.IsOpen)
            return ApplicationResult<CreateOrderResponse>.Failure(
                "RESTAURANT_UNAVAILABLE", "Restaurant is not accepting orders right now.", 400);

        if (restaurant.RegionId != command.RegionId)
            return ApplicationResult<CreateOrderResponse>.Failure(
                "REGION_MISMATCH", "Restaurant is not in your region.", 400);

        var requestedIds = command.Items.Select(i => i.MenuItemId).Distinct().ToList();
        var menuItems = await _catalog.GetMenuItemsAsync(requestedIds, cancellationToken);
        var byId = menuItems.ToDictionary(m => m.Id);

        foreach (var requested in command.Items)
        {
            if (!byId.TryGetValue(requested.MenuItemId, out var menuItem) || menuItem.RestaurantId != command.RestaurantId)
                return ApplicationResult<CreateOrderResponse>.Failure(
                    "INVALID_MENU_ITEM", $"Menu item {requested.MenuItemId} does not belong to this restaurant.", 400);
            if (!menuItem.IsAvailable)
                return ApplicationResult<CreateOrderResponse>.Failure(
                    "ITEM_UNAVAILABLE", $"'{menuItem.Name}' is currently unavailable.", 400);
        }

        var order = Order.CreateForDelivery(
            command.RiderId, command.RestaurantId, command.RegionId,
            command.DeliveryLat, command.DeliveryLng, command.PaymentMethod);

        foreach (var requested in command.Items)
            order.AddItem(requested.MenuItemId, requested.Quantity, byId[requested.MenuItemId].Price);

        order.SetDeliveryFee(_pricing.DeliveryFee);
        var surgeResult = await _surge.ResolveAsync(
            command.RegionId, "food", order.TotalAmount + order.DeliveryFee, cancellationToken);
        order.ApplySurge(surgeResult.BaseFare, surgeResult.Multiplier, surgeResult.Reason);

        var total = order.FinalFare ?? order.TotalAmount + order.DeliveryFee;
        var currency = byId[command.Items[0].MenuItemId].Currency;

        var outbox = new OrderOutboxEntry
        {
            EventType = $"order-events-{command.RegionId}",
            Payload = JsonSerializer.Serialize(new
            {
                EventName = "order_placed",
                OrderId = order.Id,
                order.RiderId,
                order.RestaurantId,
                RegionId = command.RegionId,
                Total = total,
                PaymentMethod = order.PaymentMethod.ToString(),
                OccurredAt = DateTime.UtcNow
            })
        };

        await using (var tx = await _db.Database.BeginTransactionAsync(cancellationToken))
        {
            _db.Orders.Add(order);
            _db.Set<OrderOutboxEntry>().Add(outbox);
            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }

        OrderPaymentResult payment;
        try
        {
            payment = await _payments.InitiateForOrderAsync(
                new OrderPaymentRequest(order.Id, command.RiderId, total, currency, command.PaymentMethod, command.RegionId),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Payment initiation failed for order {OrderId}; cancelling order", order.Id);
            order.TryCancel(OrderCancellationReason.PaymentFailed, null, "system", order.Version);
            _db.Set<OrderOutboxEntry>().Add(new OrderOutboxEntry
            {
                EventType = $"order-events-{command.RegionId}",
                Payload = JsonSerializer.Serialize(new
                {
                    EventName = "order_cancelled",
                    OrderId = order.Id,
                    order.RestaurantId,
                    RegionId = command.RegionId,
                    Reason = OrderCancellationReason.PaymentFailed.ToString(),
                    OccurredAt = DateTime.UtcNow
                })
            });
            await _db.SaveChangesAsync(cancellationToken);
            return ApplicationResult<CreateOrderResponse>.Failure(
                "PAYMENT_FAILED", "Payment could not be initiated; the order was cancelled.", 400);
        }

        _logger.LogInformation(
            "Order {OrderId} placed for rider {RiderId} in region {RegionId} total {Total} {Currency} payment {PaymentId}",
            order.Id, order.RiderId, command.RegionId, total, currency, payment.PaymentId);

        FareEstimate? fareResponse = null;
        if (order.BaseFare.HasValue)
        {
            fareResponse = new FareEstimate(
                order.BaseFare.Value,
                order.FinalFare!.Value,
                order.SurgeMultiplier > 1.0m ? order.SurgeMultiplier : null,
                order.SurgeReason);
        }

        return ApplicationResult<CreateOrderResponse>.Accepted(
            new CreateOrderResponse(order.Id, order.Status.ToString(), payment.PaymentId, total, fareResponse));
    }
}
```

In `src/Gruuber.Orders/OrdersModule.cs`, add the options registration after `AddDbContext` (no binder dependency — plain string parse):

```csharp
        var deliveryFee = decimal.TryParse(configuration["Orders:DeliveryFee"], out var fee) ? fee : 2.50m;
        services.AddSingleton(new Application.OrderPricingOptions(deliveryFee));
```

In `src/Gruuber.Api/Controllers/OrdersController.cs`: add `using Gruuber.SharedKernel.Payments;`; replace the `CreateOrder` action and the request records at the bottom; also update the `TransitionStatus` action's command construction (the record gained actor fields — the full guard logic lands in Task 5):

```csharp
    [HttpPost("create")]
    [Authorize(Policy = "rider")]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<PaymentMethod>(request.PaymentMethod, ignoreCase: true, out var method))
            return BadRequest(new { ErrorCode = "INVALID_PAYMENT_METHOD", ErrorMessage = "PaymentMethod must be CardMock or CashOnDelivery." });

        var cmd = new CreateOrderCommand(
            _currentUser.UserId, request.RestaurantId, _currentUser.RegionId,
            request.Items.Select(i => new OrderItemRequest(i.MenuItemId, i.Quantity)).ToList(),
            request.DeliveryLat, request.DeliveryLng, method);

        var result = await _createHandler.HandleAsync(cmd, cancellationToken);
        return result.ToHttpResult(this);
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize]
    public async Task<IActionResult> TransitionStatus(Guid id, [FromBody] TransitionOrderRequest request, CancellationToken cancellationToken)
    {
        var cmd = new TransitionOrderCommand(
            id, request.NewStatus, request.ExpectedVersion, _currentUser.RegionId,
            _currentUser.UserId, _currentUser.Role, request.Reason, request.Note);
        var result = await _transitionHandler.HandleAsync(cmd, cancellationToken);
        return result.ToHttpResult(this);
    }
```

```csharp
public record CreateOrderRequest(
    [Required] Guid RestaurantId,
    [Required][MinLength(1)] IList<OrderItemInput> Items,
    [Range(-90, 90)] double DeliveryLat,
    [Range(-180, 180)] double DeliveryLng,
    [Required] string PaymentMethod);
public record OrderItemInput(
    [Required] Guid MenuItemId,
    [Range(1, 100)] int Quantity);
public record TransitionOrderRequest(
    [Required] string NewStatus,
    [Range(1, long.MaxValue)] long ExpectedVersion,
    string? Reason = null,
    [StringLength(500)] string? Note = null);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "FullyQualifiedName~CreateOrderHandlerTests"`
Expected: `Passed! - Failed: 0, Passed: 7`

- [ ] **Step 5: Verify full build**

Run: `dotnet build src/Gruuber.Api/Gruuber.Api.csproj`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add src/Gruuber.Orders src/Gruuber.Api/Controllers/OrdersController.cs tests/Gruuber.Tests/Unit/Orders/CreateOrderHandlerTests.cs
git commit -m "feat(orders): server-side pricing and pay-at-placement order creation"
```

---

### Task 5: TransitionOrderHandler rework — legal transitions, role guards, cancellation, refund event

**Files:**
- Rewrite: `src/Gruuber.Orders/Application/Commands/TransitionOrderHandler.cs`
- Modify: `src/Gruuber.Chat/Application/ChatThreadConsumer.cs` (defensive guard — see Step 3b)
- Test: `tests/Gruuber.Tests/Unit/Orders/TransitionOrderHandlerTests.cs`
- Test: `tests/Gruuber.Tests/Unit/Chat/ChatThreadConsumerTests.cs` (add one test)

**Interfaces:**
- Consumes: `TransitionOrderCommand` (Task 4 shape), `IRestaurantCatalogReader`/`CatalogRestaurant` (Task 1), `Order.TryCancel`/`CancellationPolicy`/`OrderCancellationReason` (Task 3), `PaymentMethod` (Task 1), existing `TryTransition`/`TryAssignDriver`.
- Produces: reworked `TransitionOrderHandler` (new constructor: `(OrdersDbContext, IRestaurantCatalogReader, ILogger<TransitionOrderHandler>)`); `TransitionOrderResponse(Guid OrderId, string Status)` unchanged. Outbox events per transition: `order_accepted` / `order_preparing` / `order_ready` / `order_pickedup` / `order_delivered` / `order_cancelled` — payload carries `OrderId`, `RestaurantId`, `RiderId`, `RegionId`, `Revenue` (= `FinalFare ?? TotalAmount`), `Reason` (null unless cancelled), `OccurredAt` (analytics already consumes `order_delivered`/`order_cancelled` and reads `RestaurantId`, `RegionId`, `Revenue`). Card-paid cancellations additionally emit `payment_refund_requested` with `OrderId`, `Amount`, `Reason`, `RegionId`.
- Error codes: `INVALID_STATUS` 400, `ORDER_NOT_FOUND` 404, `INVALID_TRANSITION` 400, `FORBIDDEN` 403, `REASON_REQUIRED` 400, `INVALID_REASON` 400, `REASON_NOT_ALLOWED` 400, `RESOURCE_CONFLICTED` 409 (via `Conflict`).

- [ ] **Step 1: Write the failing tests**

`tests/Gruuber.Tests/Unit/Orders/TransitionOrderHandlerTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using Gruuber.Orders.Application.Commands;
using Gruuber.Orders.Domain;
using Gruuber.Orders.Infrastructure;
using Gruuber.SharedKernel.Catalog;
using Gruuber.SharedKernel.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class TransitionOrderHandlerTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    private static OrdersDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new OrdersDbContext(opts);
    }

    private static TransitionOrderHandler Handler(OrdersDbContext db)
    {
        var catalog = new Mock<IRestaurantCatalogReader>();
        catalog.Setup(c => c.GetRestaurantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                new CatalogRestaurant(id, OwnerId, "Kanto Grill", true, true, 1, 14.5, 120.9));
        return new TransitionOrderHandler(db, catalog.Object, NullLogger<TransitionOrderHandler>.Instance);
    }

    private static async Task<Order> SeedOrder(OrdersDbContext db, PaymentMethod method = PaymentMethod.CardMock)
    {
        var order = Order.CreateForDelivery(Guid.NewGuid(), Guid.NewGuid(), 1, 14.60, 120.98, method);
        order.AddItem(Guid.NewGuid(), 1, 100m);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private static TransitionOrderCommand Cmd(
        Guid orderId, string newStatus, long version, Guid actor, string role,
        string? reason = null, string? note = null) =>
        new(orderId, newStatus, version, 1, actor, role, reason, note);

    [TestMethod]
    public async Task Accept_ByRestaurantOwner_Succeeds()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db);
        var handler = Handler(db);

        var result = await handler.HandleAsync(Cmd(order.Id, "Accepted", 1, OwnerId, "restaurant"));

        result.IsSuccess.Should().BeTrue();
        result.Data!.Status.Should().Be("Accepted");
    }

    [TestMethod]
    public async Task Accept_ByNonOwner_Returns403()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db);
        var handler = Handler(db);

        var result = await handler.HandleAsync(Cmd(order.Id, "Accepted", 1, Guid.NewGuid(), "restaurant"));

        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("FORBIDDEN");
    }

    [TestMethod]
    public async Task Accept_ByRider_Returns403()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db);
        var handler = Handler(db);

        var result = await handler.HandleAsync(Cmd(order.Id, "Accepted", 1, order.RiderId, "rider"));

        result.StatusCode.Should().Be(403);
    }

    [TestMethod]
    public async Task IllegalTransition_PlacedToReady_Returns400()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db);
        var handler = Handler(db);

        var result = await handler.HandleAsync(Cmd(order.Id, "Ready", 1, OwnerId, "restaurant"));

        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("INVALID_TRANSITION");
    }

    [TestMethod]
    public async Task StaleVersion_Returns409()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db);
        var handler = Handler(db);

        var result = await handler.HandleAsync(Cmd(order.Id, "Accepted", 99, OwnerId, "restaurant"));

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("RESOURCE_CONFLICTED");
    }

    [TestMethod]
    public async Task PickedUp_ByAssignedDriver_Succeeds()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db);
        var driverId = Guid.NewGuid();
        order.TryTransition(OrderStatus.Accepted, 1);   // v2
        order.TryTransition(OrderStatus.Preparing, 2);  // v3
        order.TryTransition(OrderStatus.Ready, 3);      // v4
        order.TryAssignDriver(driverId, 4);             // v5
        await db.SaveChangesAsync();
        var handler = Handler(db);

        var result = await handler.HandleAsync(Cmd(order.Id, "PickedUp", 5, driverId, "driver"));

        result.IsSuccess.Should().BeTrue();
        result.Data!.Status.Should().Be("PickedUp");
    }

    [TestMethod]
    public async Task PickedUp_ByUnassignedDriver_Returns403()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db);
        order.TryTransition(OrderStatus.Accepted, 1);
        order.TryTransition(OrderStatus.Preparing, 2);
        order.TryTransition(OrderStatus.Ready, 3);
        await db.SaveChangesAsync();
        var handler = Handler(db);

        var result = await handler.HandleAsync(Cmd(order.Id, "PickedUp", 4, Guid.NewGuid(), "driver"));

        result.StatusCode.Should().Be(403);
    }

    [TestMethod]
    public async Task Cancel_ByRiderWithCustomerReason_SucceedsAndEmitsRefundEvent()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db, PaymentMethod.CardMock);
        var handler = Handler(db);

        var result = await handler.HandleAsync(
            Cmd(order.Id, "Cancelled", 1, order.RiderId, "rider", "OrderedByMistake", "fat-fingered it"));

        result.IsSuccess.Should().BeTrue();
        var updated = await db.Orders.SingleAsync();
        updated.CancellationReason.Should().Be(OrderCancellationReason.OrderedByMistake);
        updated.CancelledByRole.Should().Be("rider");
        var events = await db.Set<OrderOutboxEntry>().ToListAsync();
        events.Should().HaveCount(2);
        var names = events.Select(e => JsonDocument.Parse(e.Payload).RootElement.GetProperty("EventName").GetString()).ToList();
        names.Should().BeEquivalentTo(new[] { "order_cancelled", "payment_refund_requested" });
    }

    [TestMethod]
    public async Task Cancel_CashOnDelivery_EmitsNoRefundEvent()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db, PaymentMethod.CashOnDelivery);
        var handler = Handler(db);

        await handler.HandleAsync(Cmd(order.Id, "Cancelled", 1, order.RiderId, "rider", "OrderedByMistake"));

        var events = await db.Set<OrderOutboxEntry>().ToListAsync();
        events.Should().HaveCount(1);
        JsonDocument.Parse(events[0].Payload).RootElement.GetProperty("EventName").GetString()
            .Should().Be("order_cancelled");
    }

    [TestMethod]
    public async Task Cancel_WithoutReason_Returns400()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db);
        var handler = Handler(db);

        var result = await handler.HandleAsync(Cmd(order.Id, "Cancelled", 1, order.RiderId, "rider"));

        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("REASON_REQUIRED");
    }

    [TestMethod]
    public async Task Cancel_RiderUsingRestaurantReason_Returns400()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db);
        var handler = Handler(db);

        var result = await handler.HandleAsync(Cmd(order.Id, "Cancelled", 1, order.RiderId, "rider", "TooBusy"));

        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("REASON_NOT_ALLOWED");
    }

    [TestMethod]
    public async Task Cancel_FromReady_SystemOnly()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db);
        order.TryTransition(OrderStatus.Accepted, 1);
        order.TryTransition(OrderStatus.Preparing, 2);
        order.TryTransition(OrderStatus.Ready, 3);
        await db.SaveChangesAsync();
        var handler = Handler(db);

        var riderAttempt = await handler.HandleAsync(
            Cmd(order.Id, "Cancelled", 4, order.RiderId, "rider", "TakingTooLong"));
        riderAttempt.StatusCode.Should().Be(403);

        var systemAttempt = await handler.HandleAsync(
            Cmd(order.Id, "Cancelled", 4, Guid.Empty, "system", "NoDriverAvailable"));
        systemAttempt.IsSuccess.Should().BeTrue();
    }

    [TestMethod]
    public async Task Delivered_EmitsOrderDeliveredWithRevenue()
    {
        await using var db = CreateInMemoryDb();
        var order = await SeedOrder(db);
        var driverId = Guid.NewGuid();
        order.TryTransition(OrderStatus.Accepted, 1);
        order.TryTransition(OrderStatus.Preparing, 2);
        order.TryTransition(OrderStatus.Ready, 3);
        order.TryAssignDriver(driverId, 4);
        order.TryTransition(OrderStatus.PickedUp, 5);
        await db.SaveChangesAsync();
        var handler = Handler(db);

        var result = await handler.HandleAsync(Cmd(order.Id, "Delivered", 6, driverId, "driver"));

        result.IsSuccess.Should().BeTrue();
        var outbox = await db.Set<OrderOutboxEntry>().SingleAsync();
        using var doc = JsonDocument.Parse(outbox.Payload);
        doc.RootElement.GetProperty("EventName").GetString().Should().Be("order_delivered");
        doc.RootElement.GetProperty("RestaurantId").GetGuid().Should().Be(order.RestaurantId);
        doc.RootElement.GetProperty("Revenue").GetDecimal().Should().Be(100m);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "FullyQualifiedName~TransitionOrderHandlerTests"`
Expected: build FAILS — `TransitionOrderHandler` has no 3-arg constructor taking `IRestaurantCatalogReader`.

- [ ] **Step 3: Implement — replace `src/Gruuber.Orders/Application/Commands/TransitionOrderHandler.cs`**

```csharp
using System.Text.Json;
using Gruuber.Orders.Domain;
using Gruuber.Orders.Infrastructure;
using Gruuber.SharedKernel.Catalog;
using Gruuber.SharedKernel.Payments;
using Gruuber.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Gruuber.Orders.Application.Commands;

public class TransitionOrderHandler
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> LegalTransitions = new()
    {
        [OrderStatus.Placed] = new[] { OrderStatus.Accepted, OrderStatus.Cancelled },
        [OrderStatus.Accepted] = new[] { OrderStatus.Preparing },
        [OrderStatus.Preparing] = new[] { OrderStatus.Ready },
        [OrderStatus.Ready] = new[] { OrderStatus.PickedUp, OrderStatus.Cancelled },
        [OrderStatus.PickedUp] = new[] { OrderStatus.Delivered }
    };

    private readonly OrdersDbContext _db;
    private readonly IRestaurantCatalogReader _catalog;
    private readonly ILogger<TransitionOrderHandler> _logger;

    public TransitionOrderHandler(
        OrdersDbContext db,
        IRestaurantCatalogReader catalog,
        ILogger<TransitionOrderHandler> logger)
    {
        _db = db;
        _catalog = catalog;
        _logger = logger;
    }

    public async Task<ApplicationResult<TransitionOrderResponse>> HandleAsync(
        TransitionOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<OrderStatus>(command.NewStatus, ignoreCase: true, out var newStatus))
            return ApplicationResult<TransitionOrderResponse>.Failure(
                "INVALID_STATUS", $"Unknown status '{command.NewStatus}'.", 400);

        var order = await _db.Orders.FindAsync(new object[] { command.OrderId }, cancellationToken);
        if (order is null)
            return ApplicationResult<TransitionOrderResponse>.Failure("ORDER_NOT_FOUND", "Order not found.", 404);

        if (!LegalTransitions.TryGetValue(order.Status, out var allowed) || !allowed.Contains(newStatus))
            return ApplicationResult<TransitionOrderResponse>.Failure(
                "INVALID_TRANSITION", $"Cannot transition from {order.Status} to {newStatus}.", 400);

        var denied = await AuthorizeAsync(order, newStatus, command, cancellationToken);
        if (denied is not null)
            return denied;

        OrderCancellationReason? reason = null;
        if (newStatus == OrderStatus.Cancelled)
        {
            if (string.IsNullOrWhiteSpace(command.Reason))
                return ApplicationResult<TransitionOrderResponse>.Failure(
                    "REASON_REQUIRED", "A cancellation reason is required.", 400);
            if (!Enum.TryParse<OrderCancellationReason>(command.Reason, ignoreCase: true, out var parsed))
                return ApplicationResult<TransitionOrderResponse>.Failure(
                    "INVALID_REASON", $"Unknown cancellation reason '{command.Reason}'.", 400);
            if (!CancellationPolicy.IsAllowed(parsed, command.ActorRole))
                return ApplicationResult<TransitionOrderResponse>.Failure(
                    "REASON_NOT_ALLOWED", $"Reason '{parsed}' is not valid for role '{command.ActorRole}'.", 400);
            reason = parsed;
        }

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var transitioned = newStatus == OrderStatus.Cancelled
            ? order.TryCancel(reason!.Value, command.Note, command.ActorRole, command.ExpectedVersion)
            : order.TryTransition(newStatus, command.ExpectedVersion);
        if (!transitioned)
            return ApplicationResult<TransitionOrderResponse>.Conflict(order.Id, order.Version);

        var revenue = order.FinalFare ?? order.TotalAmount;

        _db.Set<OrderOutboxEntry>().Add(new OrderOutboxEntry
        {
            EventType = $"order-events-{command.RegionId}",
            Payload = JsonSerializer.Serialize(new
            {
                EventName = $"order_{newStatus.ToString().ToLowerInvariant()}",
                OrderId = order.Id,
                order.RestaurantId,
                order.RiderId,
                RegionId = command.RegionId,
                Revenue = revenue,
                Reason = order.CancellationReason?.ToString(),
                OccurredAt = DateTime.UtcNow
            })
        });

        if (newStatus == OrderStatus.Cancelled && order.PaymentMethod == PaymentMethod.CardMock)
        {
            _db.Set<OrderOutboxEntry>().Add(new OrderOutboxEntry
            {
                EventType = $"order-events-{command.RegionId}",
                Payload = JsonSerializer.Serialize(new
                {
                    EventName = "payment_refund_requested",
                    OrderId = order.Id,
                    Amount = revenue,
                    Reason = order.CancellationReason?.ToString(),
                    RegionId = command.RegionId,
                    OccurredAt = DateTime.UtcNow
                })
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Order {OrderId} transitioned to {Status} by {ActorRole} {ActorUserId}",
            order.Id, newStatus, command.ActorRole, command.ActorUserId);

        return ApplicationResult<TransitionOrderResponse>.Success(
            new TransitionOrderResponse(order.Id, order.Status.ToString()));
    }

    private async Task<ApplicationResult<TransitionOrderResponse>?> AuthorizeAsync(
        Order order,
        OrderStatus newStatus,
        TransitionOrderCommand command,
        CancellationToken cancellationToken)
    {
        switch (newStatus)
        {
            case OrderStatus.Accepted:
            case OrderStatus.Preparing:
            case OrderStatus.Ready:
                return await IsRestaurantOwnerAsync(order, command, cancellationToken) ? null : Forbidden();

            case OrderStatus.PickedUp:
            case OrderStatus.Delivered:
                return command.ActorRole == "driver" && order.DriverId == command.ActorUserId ? null : Forbidden();

            case OrderStatus.Cancelled when order.Status == OrderStatus.Ready:
                return command.ActorRole == "system" ? null : Forbidden();

            case OrderStatus.Cancelled: // from Placed
                if (command.ActorRole is "system" or "admin")
                    return null;
                if (command.ActorRole == "rider")
                    return order.RiderId == command.ActorUserId ? null : Forbidden();
                if (command.ActorRole == "restaurant")
                    return await IsRestaurantOwnerAsync(order, command, cancellationToken) ? null : Forbidden();
                return Forbidden();

            default:
                return Forbidden();
        }
    }

    private async Task<bool> IsRestaurantOwnerAsync(Order order, TransitionOrderCommand command, CancellationToken cancellationToken)
    {
        if (command.ActorRole != "restaurant")
            return false;
        var restaurant = await _catalog.GetRestaurantAsync(order.RestaurantId, cancellationToken);
        return restaurant is not null && restaurant.OwnerUserId == command.ActorUserId;
    }

    private static ApplicationResult<TransitionOrderResponse> Forbidden() =>
        ApplicationResult<TransitionOrderResponse>.Failure(
            "FORBIDDEN", "You are not allowed to perform this transition.", 403);
}

public record TransitionOrderResponse(Guid OrderId, string Status);
```

- [ ] **Step 3b: Guard ChatThreadConsumer against driverless order_accepted events**

Context: `ChatThreadConsumer` reacts to `EventName == "order_accepted"` and hard-reads `root.GetProperty("DriverId").GetGuid()` — written for a payload shape that nothing published until now. This task's `order_accepted` event has NO `DriverId` (dispatch assigns the driver in Phase 3), so without a guard the consumer would throw on every acceptance. In `src/Gruuber.Chat/Application/ChatThreadConsumer.cs`, add at the top of `HandleOrderAccepted` (before the existing `GetProperty` reads):

```csharp
        // Order acceptance precedes driver assignment (dispatch is a later phase);
        // the order chat thread needs both parties, so skip driverless events.
        if (!root.TryGetProperty("DriverId", out var drv) || drv.ValueKind != JsonValueKind.String)
            return;
```

Add one test to `tests/Gruuber.Tests/Unit/Chat/ChatThreadConsumerTests.cs`, following that file's existing pattern for constructing the processor/db and feeding a payload string (reuse its existing helpers verbatim — only the payload differs):

```csharp
    [TestMethod]
    public async Task OrderAccepted_WithoutDriverId_CreatesNoThreads()
    {
        // Arrange exactly as the existing order_accepted test in this file does,
        // but with a payload that has no DriverId property:
        var payload = $@"{{
            ""EventName"": ""order_accepted"",
            ""OrderId"": ""{Guid.NewGuid()}"",
            ""RiderId"": ""{Guid.NewGuid()}"",
            ""RestaurantId"": ""{Guid.NewGuid()}"",
            ""RegionId"": 1
        }}";

        // Act: process the payload through the same entry point the existing tests use.
        // Assert:
        // (await db.Threads.CountAsync()).Should().Be(0);
    }
```

(The implementer adapts the Arrange/Act lines to the file's existing helper methods — the assertion is: zero threads created, no exception thrown.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "FullyQualifiedName~TransitionOrderHandlerTests|FullyQualifiedName~ChatThreadConsumerTests"`
Expected: `Passed! - Failed: 0` — 13 new transition tests + the pre-existing Chat consumer tests + 1 new driverless-event test.

- [ ] **Step 5: Verify full build**

Run: `dotnet build src/Gruuber.Api/Gruuber.Api.csproj`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add src/Gruuber.Orders src/Gruuber.Chat tests/Gruuber.Tests/Unit/Orders/TransitionOrderHandlerTests.cs tests/Gruuber.Tests/Unit/Chat/ChatThreadConsumerTests.cs
git commit -m "feat(orders): role-guarded transitions with cancellation reasons and refund event"
```

---

### Task 6: Restaurant incoming-orders list

**Files:**
- Modify: `src/Gruuber.Orders/Application/Queries/OrderQueries.cs` (append records)
- Create: `src/Gruuber.Orders/Application/Queries/GetRestaurantOrdersHandler.cs`
- Modify: `src/Gruuber.Orders/OrdersModule.cs` (register handler)
- Modify: `src/Gruuber.Api/Controllers/RestaurantsController.cs` (add endpoint)
- Test: `tests/Gruuber.Tests/Unit/Orders/GetRestaurantOrdersHandlerTests.cs`

**Interfaces:**
- Consumes: `Order.CreateForDelivery` (Task 3), `OrdersDbContext`; on the controller side, the existing `RestaurantQueryHandler.GetMineAsync(Guid, CancellationToken)` from Phase 1 (resolves the caller's restaurant, 404 `NOT_FOUND` if none — its `RestaurantDetailResponse.Id` is the restaurant id).
- Produces:
  - `GetRestaurantOrdersQuery(Guid RestaurantId, string? Status, int Page, int PageSize)`
  - `RestaurantOrderSummary(Guid OrderId, string Status, decimal Total, string PaymentMethod, DateTime CreatedAt, long Version)` — `Version` included so the owner can supply `ExpectedVersion` when accepting.
  - `PagedOrders(IReadOnlyList<RestaurantOrderSummary> Items, int Page, int PageSize, int TotalCount)`
  - `GetRestaurantOrdersHandler.HandleAsync(query, ct)` → `ApplicationResult<PagedOrders>`; invalid status → 400 `INVALID_STATUS`; ordered by `CreatedAt` descending; page ≥ 1, pageSize clamped 1..50.
  - Endpoint: `GET /v1/restaurants/mine/orders?status=&page=&pageSize=` `[Authorize(Policy = "restaurant")]`.

- [ ] **Step 1: Write the failing tests**

`tests/Gruuber.Tests/Unit/Orders/GetRestaurantOrdersHandlerTests.cs`:

```csharp
using FluentAssertions;
using Gruuber.Orders.Application.Queries;
using Gruuber.Orders.Domain;
using Gruuber.Orders.Infrastructure;
using Gruuber.SharedKernel.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class GetRestaurantOrdersHandlerTests
{
    private static OrdersDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new OrdersDbContext(opts);
    }

    private static Order NewOrder(Guid restaurantId)
    {
        var order = Order.CreateForDelivery(Guid.NewGuid(), restaurantId, 1, 14.6, 120.98, PaymentMethod.CardMock);
        order.AddItem(Guid.NewGuid(), 1, 100m);
        return order;
    }

    [TestMethod]
    public async Task List_ReturnsOnlyThisRestaurantsOrders_NewestFirst()
    {
        await using var db = CreateInMemoryDb();
        var restaurantId = Guid.NewGuid();
        var mine = NewOrder(restaurantId);
        var other = NewOrder(Guid.NewGuid());
        db.Orders.AddRange(mine, other);
        await db.SaveChangesAsync();
        var handler = new GetRestaurantOrdersHandler(db);

        var result = await handler.HandleAsync(new GetRestaurantOrdersQuery(restaurantId, null, 1, 20));

        result.IsSuccess.Should().BeTrue();
        result.Data!.TotalCount.Should().Be(1);
        result.Data.Items.Single().OrderId.Should().Be(mine.Id);
        result.Data.Items.Single().Total.Should().Be(100m);
        result.Data.Items.Single().Version.Should().Be(1);
    }

    [TestMethod]
    public async Task List_FiltersByStatus()
    {
        await using var db = CreateInMemoryDb();
        var restaurantId = Guid.NewGuid();
        var placed = NewOrder(restaurantId);
        var accepted = NewOrder(restaurantId);
        accepted.TryTransition(OrderStatus.Accepted, 1);
        db.Orders.AddRange(placed, accepted);
        await db.SaveChangesAsync();
        var handler = new GetRestaurantOrdersHandler(db);

        var result = await handler.HandleAsync(new GetRestaurantOrdersQuery(restaurantId, "Placed", 1, 20));

        result.Data!.TotalCount.Should().Be(1);
        result.Data.Items.Single().OrderId.Should().Be(placed.Id);
        result.Data.Items.Single().Status.Should().Be("Placed");
    }

    [TestMethod]
    public async Task List_InvalidStatus_Returns400()
    {
        await using var db = CreateInMemoryDb();
        var handler = new GetRestaurantOrdersHandler(db);

        var result = await handler.HandleAsync(new GetRestaurantOrdersQuery(Guid.NewGuid(), "NotAStatus", 1, 20));

        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("INVALID_STATUS");
    }

    [TestMethod]
    public async Task List_Paginates()
    {
        await using var db = CreateInMemoryDb();
        var restaurantId = Guid.NewGuid();
        for (var i = 0; i < 25; i++)
            db.Orders.Add(NewOrder(restaurantId));
        await db.SaveChangesAsync();
        var handler = new GetRestaurantOrdersHandler(db);

        var page2 = await handler.HandleAsync(new GetRestaurantOrdersQuery(restaurantId, null, 2, 10));

        page2.Data!.TotalCount.Should().Be(25);
        page2.Data.Items.Should().HaveCount(10);
        page2.Data.Page.Should().Be(2);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "FullyQualifiedName~GetRestaurantOrdersHandlerTests"`
Expected: build FAILS — `GetRestaurantOrdersHandler` does not exist.

- [ ] **Step 3: Implement**

Append to `src/Gruuber.Orders/Application/Queries/OrderQueries.cs`:

```csharp
public record GetRestaurantOrdersQuery(Guid RestaurantId, string? Status, int Page, int PageSize);

public record RestaurantOrderSummary(
    Guid OrderId,
    string Status,
    decimal Total,
    string PaymentMethod,
    DateTime CreatedAt,
    long Version);

public record PagedOrders(IReadOnlyList<RestaurantOrderSummary> Items, int Page, int PageSize, int TotalCount);
```

`src/Gruuber.Orders/Application/Queries/GetRestaurantOrdersHandler.cs` (new):

```csharp
using Gruuber.Orders.Domain;
using Gruuber.Orders.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Orders.Application.Queries;

public class GetRestaurantOrdersHandler
{
    private readonly OrdersDbContext _db;

    public GetRestaurantOrdersHandler(OrdersDbContext db)
    {
        _db = db;
    }

    public async Task<ApplicationResult<PagedOrders>> HandleAsync(
        GetRestaurantOrdersQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 50);

        var dbQuery = _db.Orders.AsNoTracking().Where(o => o.RestaurantId == query.RestaurantId);

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<OrderStatus>(query.Status, ignoreCase: true, out var status))
                return ApplicationResult<PagedOrders>.Failure(
                    "INVALID_STATUS", $"Status must be one of: {string.Join(", ", Enum.GetNames<OrderStatus>())}.", 400);
            dbQuery = dbQuery.Where(o => o.Status == status);
        }

        var total = await dbQuery.CountAsync(cancellationToken);
        var items = await dbQuery
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new RestaurantOrderSummary(
                o.Id,
                o.Status.ToString(),
                o.FinalFare ?? o.TotalAmount,
                o.PaymentMethod.ToString(),
                o.CreatedAt,
                o.Version))
            .ToListAsync(cancellationToken);

        return ApplicationResult<PagedOrders>.Success(new PagedOrders(items, page, pageSize, total));
    }
}
```

In `src/Gruuber.Orders/OrdersModule.cs` add:

```csharp
        services.AddScoped<GetRestaurantOrdersHandler>();
```

In `src/Gruuber.Api/Controllers/RestaurantsController.cs`: add `using Gruuber.Orders.Application.Queries;`, add a constructor-injected field `GetRestaurantOrdersHandler _restaurantOrdersHandler` (same pattern as the existing fields), and add the endpoint:

```csharp
    [HttpGet("mine/orders")]
    [Authorize(Policy = "restaurant")]
    public async Task<IActionResult> GetMineOrders(
        [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var mine = await _queryHandler.GetMineAsync(_currentUser.UserId, cancellationToken);
        if (!mine.IsSuccess)
            return mine.ToHttpResult(this);

        var result = await _restaurantOrdersHandler.HandleAsync(
            new GetRestaurantOrdersQuery(mine.Data!.Id, status, page, pageSize), cancellationToken);
        return result.ToHttpResult(this);
    }
```

(Route note: `mine/orders` is a literal template — no conflict with `{id:guid}` routes.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "FullyQualifiedName~GetRestaurantOrdersHandlerTests"`
Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 5: Run the full suite and build (end-of-phase regression gate)**

Run: `dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj`
Expected: all pass, 0 failed (previous 210 + ~35 new; 22 pre-existing skips).
Run: `dotnet build src/Gruuber.Api/Gruuber.Api.csproj`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add src/Gruuber.Orders src/Gruuber.Api/Controllers/RestaurantsController.cs tests/Gruuber.Tests/Unit/Orders/GetRestaurantOrdersHandlerTests.cs
git commit -m "feat(orders): restaurant incoming-orders list endpoint"
```

---

## Verification (end of phase)

1. `dotnet build src/Gruuber.Api/Gruuber.Api.csproj` — clean build.
2. `dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj` — full suite green.
3. Manual smoke (local Postgres; apply migrations: `dotnet ef database update --project src/Gruuber.Payments` and `--project src/Gruuber.Orders`):
   - Rider: `POST /v1/orders/create` with `{RestaurantId, Items:[{MenuItemId, Quantity}], DeliveryLat, DeliveryLng, PaymentMethod:"CashOnDelivery"}` → 202 with server-computed `Total` and `PaymentId`; `GET /v1/payments/{paymentId}` shows `OrderId` + `Method`.
   - Owner: `GET /v1/restaurants/mine/orders?status=Placed` → order listed with `Version`; `PATCH /v1/orders/{id}/status` `{NewStatus:"Accepted", ExpectedVersion}` → 200; non-owner attempt → 403.
   - Owner reject: `{NewStatus:"Cancelled", ExpectedVersion, Reason:"TooBusy"}` on a card order → 200 and `payment_refund_requested` row appears in `order_outbox`.
   - Rider cancel with `Reason:"TooBusy"` → 400 `REASON_NOT_ALLOWED`.

## Out of Scope (Phase 3 — see spec §6)

- Dispatch saga (Kafka consumer creating `RideType="food"` rides on `order_accepted`, writing `RideId` back).
- `OrderAcceptanceTimeoutWorker` (auto-cancel `Placed` after window with `RestaurantUnresponsive`).
- Consumers for `payment_refund_requested` (actual refund execution) and COD auto-confirm on delivery.
- No-driver retry/cancellation flow (`NoDriverAvailable` emission).
