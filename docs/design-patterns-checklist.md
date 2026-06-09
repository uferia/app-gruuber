# Design Patterns Checklist — Gruuber

> Evaluated against the codebase as of June 2026.  
> Legend: ✅ Applied · ⚠️ Partial · ❌ Missing (but applicable) · N/A Not relevant to this project

---

## Creational Patterns

| # | Pattern | Purpose | Status | Evidence / Notes |
|---|---------|---------|--------|-----------------|
| 1 | **Singleton** | Ensure a class has only one instance | ✅ | `AddSingleton` DI registrations for `IKafkaProducer`, `ICurrentUserContext`, Redis, etc. |
| 2 | **Factory Method** | Define an interface for creating objects | ✅ | `RideOutboxFactory.cs` creates outbox messages per event type |
| 3 | **Abstract Factory** | Create families of related objects | ✅ | `IEventMessageFactory<TOutboxEntry>` in SharedKernel; `RideOutboxFactory` and `OrderOutboxFactory` implement it to produce region-scoped outbox entries |
| 4 | **Builder** | Separate construction of complex objects from representation | ✅ | `RideBuilder` (`Gruuber.Rides.Domain`) and `OrderBuilder` (`Gruuber.Orders.Domain`) — fluent API covering all optional fields with validation guards |
| 5 | **Prototype** | Create new objects by cloning existing ones | N/A | No cloning use-case identified |
| 6 | **Object Pool** | Reuse expensive objects instead of creating new | ⚠️ | Kafka/Redis libraries manage pools internally; no explicit wrapper |
| 7 | **Lazy Initialization** | Delay object creation until needed | ⚠️ | Not explicitly applied; could benefit expensive per-region service init |
| 27 | **Static Factory Method** | Provide object creation via static method | ✅ | `Result<T>.Ok()` / `Result<T>.Fail()` aliases added in `Result.cs`; complement existing `Success()` / `Failure()` |

---

## Structural Patterns

| # | Pattern | Purpose | Status | Evidence / Notes |
|---|---------|---------|--------|-----------------|
| 8 / 28 | **Adapter (Object)** | Convert/compose interface of a class into another | ✅ | `DefaultDriverScoringService` adapts `IDriverScoringService`; YARP adapts upstream routes |
| 29 | **Adapter (Class)** | Use inheritance to adapt incompatible interfaces | N/A | Composition preferred in C# |
| 9 | **Bridge** | Decouple abstraction from implementation | ✅ | `IKafkaProducer` / `KafkaProducer`; `IOutboxPublisher` / `OutboxWorker` |
| 10 | **Composite** | Compose objects into tree structures (part-whole) | ✅ | `Order` contains `OrderItem[]`; `SurgePricingConfig` contains `SurgeTimeRule[]` |
| 11 / 30 | **Decorator** | Add responsibilities to objects dynamically | ✅ | ASP.NET middleware pipeline is decorator-like; **MediatR Pipeline Behaviors** wired: `LoggingBehavior`, `ValidationBehavior`, `ErrorHandlingBehavior` in `SharedKernel/Messaging/Pipeline/` |
| 12 / 31 | **Facade** | Simplified interface to a complex subsystem | ✅ | `RideRequestCoordinator`, `DriverMatchCoordinator`, `PoolMatchCoordinator`, `*Module.cs` registration classes |
| 13 | **Flyweight** | Share objects to support large numbers efficiently | N/A | No high-cardinality shared object scenario identified |
| 14 | **Proxy** | Surrogate or placeholder for another object | ✅ | `RedisRateLimiterMiddleware` is a proxy gate; YARP is a reverse proxy for JWT routing |
| 34 | **View Helper** | Separate presentation logic from business logic | ✅ | `RideView.cs`, `PaymentOutboxEntry.cs` separate read/write models |

---

## Behavioural Patterns

| # | Pattern | Purpose | Status | Evidence / Notes |
|---|---------|---------|--------|-----------------|
| 15 | **Strategy** | Family of interchangeable algorithms | ✅ | `IDriverScoringService` / `DefaultDriverScoringService`; `ISurgePricingService` / `SurgePricingService` |
| 16 | **Observer** | Notify multiple objects about state changes | ✅ | Kafka event bus; SignalR `ILocationBroadcaster` for live ride tracking |
| 17 | **Command** | Encapsulate a request as an object | ✅ | MediatR: `RequestRideHandler`, `MatchDriverHandler`, `TransitionRideHandler`, `AcceptSoloUpgradeHandler` |
| 18 | **Iterator** | Access elements of a collection sequentially | ✅ | C# `IEnumerable<T>` / LINQ throughout |
| 19 | **Mediator** | Centralize complex communication between objects | ✅ | **MediatR** library wired in all modules |
| 20 | **Memento** | Capture and restore an object's state | ✅ | `ISnapshot<TId>` / `ISnapshotOriginator<T>` / `ISnapshotRepository<T,TId>` in SharedKernel; `RideSnapshot` and `OrderSnapshot` records; `Ride` and `Order` implement `ISnapshotOriginator` |
| 21 | **State** | Object changes behaviour when state changes | ✅ | `IRideState` / `RideStateBase` / concrete state classes (`RequestedState`, `MatchedState`, …) in `Gruuber.Rides.Domain.States`; `IOrderState` / `OrderStateBase` in `Gruuber.Orders.Domain.States`; `RideStateFactory` / `OrderStateFactory` enforce legal transitions |
| 22 | **Template Method** | Skeleton of an algorithm in a base method | ⚠️ | `IExponentialBackoff` defines the interface; concrete `ExponentialBackoff.cs` exists but no abstract base class enforcing the skeleton |
| 32 | **Filter / Criteria** | Select objects that meet certain criteria | ✅ | `ISpecification<T>` + `AndSpecification`, `OrSpecification`, `NotSpecification` composites in SharedKernel; `DriverMatchEligibilitySpecification` (radius, rating, availability) in Rides; `OrderEligibilitySpecification` (restaurant open, items, min basket) in Orders |

---

## Concurrency Patterns

| # | Pattern | Purpose | Status | Evidence / Notes |
|---|---------|---------|--------|-----------------|
| 36 | **Active Object** | Async method execution decoupled from invocation | ⚠️ | `PaymentTimeoutWorker`, `PoolTimeoutWorker` are background workers but not formal Active Objects |
| 37 | **Reactor** | Handle service requests via one or more input sources | ✅ | Kafka consumers react to `ride-events-{region}`, `payment-events-{region}`, etc. |
| 38 | **Proactor** | Separate initiation of an operation from its completion | ✅ | Outbox pattern: write to `outbox` → `OutboxWorker` publishes → callback sets final state |
| 39 | **Monitor Object** | Locking mechanism around critical sections | ✅ | Redis Lua scripts provide atomic critical sections for token bucket and GEO ops |
| 40 | **Thread Pool** | Reuse a pool of threads to execute tasks | ✅ | .NET `ThreadPool` + `IHostedService` background workers |

---

## Architectural Patterns

| # | Pattern | Purpose | Status | Evidence / Notes |
|---|---------|---------|--------|-----------------|
| 41 | **MVC** | Separate data, UI, and control | ✅ | ASP.NET Core controllers + Domain models + View DTOs |
| 42 | **MVP** | Separate UI logic from presentation logic | N/A | API-only project |
| 43 | **MVVM** | Bind UI with ViewModel | N/A | API-only project |
| 44 | **Layered Architecture** | Layers: presentation, business, data | ✅ | `Controllers` → `Application` → `Domain` → `Infrastructure` in every module |
| 45 | **Microkernel** | Minimal core with plugins | ⚠️ | `*Module.cs` registration classes are plugin-like but no true extension-point interface |
| 46 | **SOA** | System as a collection of services | ⚠️ | Modular monolith; each module is a proto-service; full SOA is the future microservice split |
| 47 | **CQRS** | Separate read (query) and write (command) operations | ✅ | **Explicitly architected**: `Commands/` + `Queries/` folders; `ride_views` async via Kafka |

---

## Data & Integration Patterns

| # | Pattern | Purpose | Status | Evidence / Notes |
|---|---------|---------|--------|-----------------|
| 23 | **Multiton** | Multiple singletons keyed by identifier | ✅ | `IRegionClientRegistry<TClient>` in SharedKernel; `RegionedRedisDatabaseRegistry` (keyed `IDatabase` per region) and `RegionedKafkaProducerRegistry` (keyed `IKafkaProducer` per region) in `Gruuber.Api.Infrastructure` |
| 24 / 25 | **Factory Object / Object Factory** | Centralise object creation | ✅ | `RideOutboxFactory`; DI container acts as object factory |
| 26 | **Parameterized Constructor** | Create objects with different parameters | ✅ | Standard C# — all domain entities use constructor injection |
| 33 | **Private Class Data** | Restrict access to class data | ✅ | C# `private`/`init`-only properties on entities |
| 35 | **Data Mapper** | Map data from object to database | ✅ | Entity Framework Core ORM |
| 48 | **Dependency Injection** | Inject dependencies instead of hard-coding | ✅ | ASP.NET Core built-in DI container throughout all modules |
| 49 | **Inversion of Control** | Transfer control of object creation | ✅ | DI container + interfaces in every module |
| 50 | **Service Locator** | Centralised registry to locate services | N/A | Anti-pattern — intentionally avoided in favour of DI |
| 51 | **Repository** | Abstract data access logic | ✅ | `IRideRepository` defined; other modules rely on EF Core directly |
| 52 | **Unit of Work** | Maintain a list of objects and coordinate changes | ✅ | `IUnitOfWork` interface in SharedKernel; `RidesUnitOfWork` wraps `RidesDbContext` + outbox; `OrdersUnitOfWork` wraps `OrdersDbContext` + outbox — guarantees atomic commit of domain writes and events |
| 53 | **DAO** | Encapsulate data access logic | ✅ | Repository pattern covers this |
| 54 | **Business Delegate** | Delegate business logic to appropriate services | ⚠️ | Coordinator classes partially serve this role; no explicit delegate abstraction |

---

## Summary

| Status | Count |
|--------|-------|
| ✅ Applied | 35 |
| ⚠️ Partial | 9 |
| ❌ Missing (applicable) | 0 |
| N/A | 9 |

### Implemented (previously missing)
- [x] Abstract Factory — `IEventMessageFactory<TOutboxEntry>`, `RideOutboxFactory`, `OrderOutboxFactory`
- [x] Builder — `RideBuilder`, `OrderBuilder`
- [x] Static Factory Method — `Result<T>.Ok()` / `Result<T>.Fail()`
- [x] Decorator (MediatR Pipeline Behaviors) — `LoggingBehavior`, `ValidationBehavior`, `ErrorHandlingBehavior`
- [x] Memento — `ISnapshot<TId>`, `ISnapshotOriginator<T>`, `RideSnapshot`, `OrderSnapshot`
- [x] State (formal) — `IRideState`/`IOrderState` with concrete states and `*StateFactory`
- [x] Specification (Filter/Criteria) — `ISpecification<T>` composites, `DriverMatchEligibilitySpecification`, `OrderEligibilitySpecification`
- [x] Multiton — `IRegionClientRegistry<T>`, `RegionedRedisDatabaseRegistry`, `RegionedKafkaProducerRegistry`
- [x] Unit of Work (explicit interface) — `IUnitOfWork`, `RidesUnitOfWork`, `OrdersUnitOfWork`
