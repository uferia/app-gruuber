# Food Delivery Vertical (GrabFood / Uber Eats parity) — Design

**Date:** 2026-07-04
**Status:** Approved by user (brainstorming session)
**Scope:** Backend food-delivery vertical on the existing Gruuber modular monolith, in three phases.

## 1. Problem

Gruuber already has Rides (matching, pooling, surge), an Orders skeleton, Payments, Tracking, Chat, Analytics, and Auth. What it lacks to work like GrabFood / Uber Eats is the restaurant vertical (deep-analysis item #8, mobile analysis §3.6):

- No `Restaurant` or `MenuItem` entity — `RestaurantId`/`MenuItemId` on orders are unvalidated GUIDs.
- Clients dictate item prices at order creation (P0 monetary-integrity hole #4).
- No discovery surface (browse restaurants / menus).
- `CreateOrderRequest` requires a `RideId` up front; real food delivery dispatches the courier after the restaurant accepts.
- No restaurant-facing workflow: no incoming-order list, no accept/reject/ready flow, no open/closed state.
- No cancellation reasons: `Cancelled` records nothing about who cancelled or why.

## 2. Decisions (from brainstorming)

| Decision | Choice |
|---|---|
| Scope | Full food vertical, phased; each phase independently shippable |
| P0 backlog | Overlap only — this work fixes the P0 items it naturally touches (server-side pricing, ownership checks on new/changed endpoints); the rest of the P0 list stays separate |
| Courier dispatch | Saga: on `order_accepted`, create a ride with `RideType="food"` through the existing `RideRequestCoordinator`; same driver pool, matching, and tracking |
| Restaurant onboarding | Self-register + admin approval, mirroring the driver pattern (`DriverApprovalStatus`, `AdminDriverController`) |
| Discovery | Region list + haversine distance sort + name/cuisine search + open-now filter + pagination |
| Payment | Pay at placement; add `PaymentMethod` (`CardMock`, `CashOnDelivery`) |
| Module placement | New `Gruuber.Restaurants` module (Approach A); catalog read contract exposed via SharedKernel interface |

## 3. Architecture

New project **`src/Gruuber.Restaurants/`** with the standard module shape (Domain / Application / Infrastructure / Migrations), its own `RestaurantsDbContext`, and `RestaurantsModule.AddRestaurantsModule(config)` registered in `Program.cs` alongside the other seven modules.

**Cross-module contract:** SharedKernel gains **`IRestaurantCatalogReader`** (precedent: `ISurgePricingService`), implemented in Restaurants, consumed in-process by Orders. It answers: restaurant approval status, open/closed, region, coordinates; and menu items (price, currency, availability, owning restaurant) by ID set. Orders never references the Restaurants project directly.

**No Restaurants outbox in Phase 1** — nothing consumes restaurant events yet. Add it only when something does.

Auth gains a **`restaurant` role**; one restaurant per owner account in v1.

## 4. Phase 1 — Catalog & Onboarding

### Domain

- **`Restaurant`** (extends `EntityBase`; optimistic-concurrency `Version` included): `OwnerUserId`, `Name`, `Description`, `CuisineType`, `RegionId`, `Lat`, `Lng`, `Address`, `ApprovalStatus` (`Pending` / `Approved` / `Rejected` / `Suspended`), `IsOpen` (manual toggle; no opening-hours schedule in v1).
- **`MenuItem`**: `RestaurantId`, `Name`, `Description`, `Category`, `Price`, `Currency`, `IsAvailable`.

### Endpoints (all `/v1`)

| Endpoint | Actor | Purpose |
|---|---|---|
| `POST /v1/restaurants/register` | restaurant role | Submit restaurant (starts `Pending`) |
| `GET /v1/restaurants/mine` | owner | Own restaurant + approval status |
| `PATCH /v1/restaurants/{id}` | owner | Edit profile (version-checked) |
| `PATCH /v1/restaurants/{id}/open` | owner | Open/close toggle |
| `POST /v1/restaurants/{id}/menu-items` | owner | Add menu item |
| `PATCH /v1/restaurants/{id}/menu-items/{itemId}` | owner | Edit item (price, availability, …) |
| `DELETE /v1/restaurants/{id}/menu-items/{itemId}` | owner | Remove item |
| `GET /v1/restaurants?lat&lng&search&openNow&page&pageSize` | any authenticated | Discovery |
| `GET /v1/restaurants/{id}`, `GET /v1/restaurants/{id}/menu` | any authenticated | Detail + menu browse |
| `GET /v1/admin/restaurants?status=` | admin | Approval queue |
| `POST /v1/admin/restaurants/{id}/approve`, `.../reject` | admin | Approve / reject |

Discovery rules: approved restaurants only; region always taken from the caller's JWT claims, never the request body; distance = haversine on stored lat/lng computed in the query (restaurants are static — no Redis GEO); `search` matches name or cuisine; paginated.

Every mutation checks ownership (owner of that restaurant, or admin). Responses use `ApplicationResult<T>`; version mismatch returns `409 RESOURCE_CONFLICTED`.

## 5. Phase 2 — Order Rework & Payment

### Order creation

`POST /v1/orders/create` request becomes: `RestaurantId`, `Items[] (MenuItemId, Quantity)`, `DeliveryLat`, `DeliveryLng`, `PaymentMethod`. **`RideId` and client-supplied prices are removed.**

Handler flow, via `IRestaurantCatalogReader`:
1. Restaurant is `Approved` and `IsOpen`.
2. Every item exists, `IsAvailable`, and belongs to that restaurant.
3. Compute server-side: `Total = (Subtotal from menu prices + flat DeliveryFee from config) × food-surge multiplier` (existing `ISurgePricingService`, `RideType="food"`). Stored on the order.

`Order` gains: nullable `RideId` (set by Phase 3 dispatch), `DeliveryLat`/`DeliveryLng`, `PaymentMethod`, cancellation fields (§5.3).

### Restaurant order management

- **New:** `GET /v1/restaurants/mine/orders?status=&page=` — incoming orders for the owner's restaurant (`status=Placed` = awaiting acceptance).
- **Reused:** `PATCH /v1/orders/{id}/status` becomes role-guarded per transition:

| Transition | Allowed actor |
|---|---|
| `Placed → Accepted` | restaurant owner (of the order's restaurant) |
| `Placed → Cancelled` | restaurant owner (reject) or customer (cancel before acceptance) |
| `Accepted → Preparing → Ready` | restaurant owner |
| `Ready → PickedUp → Delivered` | assigned driver |

`Cancelled` is reachable from: `Placed` (customer, restaurant owner, or system — acceptance timeout / payment failure) and `Ready` (system only — no driver available, Phase 3). User-initiated cancellation after acceptance is out of scope for v1.

### Cancellation reasons

New fields on `Order`: `CancellationReason` (enum, null unless `Cancelled`), `CancellationNote` (optional, max 500 chars), `CancelledByRole` (`customer` / `restaurant` / `system` / `admin`). The transition request gains an optional `Reason`, **required when `NewStatus = Cancelled`**, validated against the actor:

- **Restaurant:** `ItemUnavailable`, `TooBusy`, `ClosingSoon`, `MenuPriceIncorrect`, `CannotFulfillInstructions`, `TechnicalIssue`
- **Customer** (only while `Placed`): `OrderedByMistake`, `WrongAddress`, `TakingTooLong`, `PaymentIssue`, `DuplicateOrder`
- **System** (workers/sagas only): `RestaurantUnresponsive`, `NoDriverAvailable`, `PaymentFailed`

### Payment

Payments module gains `PaymentMethod` (`CardMock`, `CashOnDelivery`) and a nullable `OrderId` alongside `RideId`.

- **CardMock:** payment initiated at placement for the server-computed total; existing confirm/timeout machinery applies. Placement failure/timeout cancels the order with `PaymentFailed`.
- **CashOnDelivery:** payment record created at placement, confirmed by the driver at the `Delivered` transition.
- **Refunds:** any cancellation of a card-paid order before `PickedUp` publishes the full-refund compensation event (CLAUDE.md 9.2). All-or-nothing in v1; reason codes are for analytics/support, not refund math. COD cancellations move no money.

## 6. Phase 3 — Dispatch & Delivery Lifecycle

- **Dispatch saga:** Kafka consumer on order events. On `order_accepted`: create a ride via `RideRequestCoordinator` with `RideType="food"`, pickup = restaurant coordinates, destination = order delivery coordinates; write the resulting `RideId` back to the order. Matching, tracking, and SignalR location updates reuse existing machinery unchanged.
- **Order lifecycle events** (`order_placed`, `order_accepted`, `order_rejected`, `order_ready`, …) flow through the existing `OrderOutbox`.
- **`OrderAcceptanceTimeoutWorker`:** auto-cancels `Placed` orders older than a configurable window (default 10 minutes) with `RestaurantUnresponsive`; follows the `PoolTimeoutWorker` / `ChatThreadClosureWorker` pattern.
- **No-driver handling:** if matching fails, the order stays `Ready` and dispatch retries with backoff; after retries are exhausted, the order is cancelled with `NoDriverAvailable` (triggering the card refund path). No new failure state in v1.

## 7. Error Handling & Observability

Per CLAUDE.md throughout: `ApplicationResult<T>` responses; `409 RESOURCE_CONFLICTED` on version mismatch; Kafka consumers retry with exponential backoff + jitter, max 5 attempts, then DLQ; structured Serilog logs carrying `TraceId`, `OrderId`, `RideId`, and `RestaurantId`; endpoints within existing latency SLAs.

## 8. Testing

MSTest + FluentAssertions + Moq (repo convention). Unit coverage for: registration/approval handlers; discovery filtering (region scoping, approved-only, open-now, search); server-side pricing math (menu prices + delivery fee + surge); the full transition role matrix including cancellation-reason/actor validation; dispatch consumer behavior (mocked Kafka + coordinator); both payment-method flows including refund-event publication.

## 9. Out of Scope

- Ratings, promotions, featured rows, delivery-time estimates (discovery "full search experience" — later phase).
- Opening-hours schedules (manual open/closed toggle only).
- Partial refunds; refund math beyond all-or-nothing.
- Order status push over SignalR (platform-wide gap, deep-analysis #14 — separate work).
- The remaining P0 backlog items not naturally touched by this vertical.
- Mobile/frontend clients (see `mobile-app-analysis-2026-07-03.md`).
