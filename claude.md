This file defines the architectural context, constraints, and patterns for the RideShare Food Delivery project. Follow
   these instructions strictly when generating code, reviewing PRs, or answering questions about the system.

  ---
  1. Project Overview

  This is a high-concurrency, real-time ride-sharing and food delivery platform. It handles ride requests, driver
  matching, order creation, payment processing, and real-time location tracking. The system is transitioning from a
  monolith to a modular microservices architecture using CQRS principles.

  Core Goals:
  - High availability with fault tolerance.
  - Real-time tracking (SignalR).
  - Resilience against external provider failures (Payments, Providers).
  - Strict consistency for state changes (Rides, Orders, Payments).

  ---
  2. Technology Stack

  You are working with ASP.NET Core 8. Ensure all new code targets this framework.

  ┌───────────┬───────────────────────────┬─────────────────────────────────────────────────┐
  │ Component │        Technology         │                      Usage                      │
  ├───────────┼───────────────────────────┼─────────────────────────────────────────────────┤
  │ Web/API   │ ASP.NET Core MVC, Web API │ REST endpoints.                                 │
  ├───────────┼───────────────────────────┼─────────────────────────────────────────────────┤
  │ Database  │ PostgreSQL                │ Primary store (Rides, Orders, Payments, Users). │
  ├───────────┼───────────────────────────┼─────────────────────────────────────────────────┤
  │ Cache/Geo │ Redis                     │ Driver location GEO (TTL: 10s), Session State.  │
  ├───────────┼───────────────────────────┼─────────────────────────────────────────────────┤
  │ Messaging │ Apache Kafka              │ Event Bus for domain events (Outbox pattern).   │
  ├───────────┼───────────────────────────┼─────────────────────────────────────────────────┤
  │ Real-time │ Microsoft SignalR         │ Live location updates to client apps.           │
  ├───────────┼───────────────────────────┼─────────────────────────────────────────────────┤
  │ Auth      │ JWT (Access/Refresh)      │ Authorization header with scope: rides.read.    │
  ├───────────┼───────────────────────────┼─────────────────────────────────────────────────┤
  │ ORM       │ EF Core                   │ Migrations via EF or Flyway.                    │
  └───────────┴───────────────────────────┴─────────────────────────────────────────────────┘

  ---
  3. Architecture & Patterns

  3.1 Event-Driven Design

  - Outbox Pattern: Use RideOutbox, OrderOutbox tables to ensure events are persisted before sending to Kafka. Do not
  publish events directly from the application layer.
  - Sagas: Use distributed sages for order lifecycle (Create -> Assign -> Complete/Fail). Compensation events must be
  defined in the contract (e.g., refund payment).
  - CQRS: Read models (ride_views) are denormalized via Kafka consumers. Do not query ride_views for transactional
  writes.

  3.2 Resilience & Fault Tolerance

  - Kafka Consumer: Implement IExponentialBackoff with Jitter. Retry logic: Max 5 attempts. If failed, route to DLQ
  (Dead Letter Queue) topic.
  - Payments: Handle timeouts by polling for webhook status. If payment times out, trigger a compensating event
  (refund).
  - Redis: If RideView stale data is detected (Geo mismatch), mark driver offline and atomically remove from Geo.
  - Circuit Breakers: Wrap external provider calls with fallback logic (e.g., /rides/{id} fallback).

  3.3 Concurrency

  - Optimistic Concurrency: All critical updates (Ride, Order, Payment) use version column (timestamp or integer). Check
   versions before UPDATE. If mismatch, return 409 Conflict with error code RESOURCE_CONFLICTED.
  - Locking: Use database row locks (FOR UPDATE) only for short-duration background jobs, not for web endpoints.

  ---
  4. API & Endpoint Guidelines

  4.1 Versioning

  - Strategy: URL-based versioning.
  - Format: /v1/rides/request, /v1/orders/create.
  - Requirement: New endpoints must include /v1/ prefix. Do not modify existing endpoints.

  4.2 Latency Targets

  All endpoints must adhere to these SLAs:
  - P50 Latency: < 200ms
  - P95 Latency: < 500ms
  - P99 Latency: < 1s
  - Timeout: 2 seconds for web requests.

  4.3 Error Handling

  - Standard: Use ApplicationResult<T> or Result<T> pattern for API responses.
  - Kafka Errors: ConsumeResult failures must trigger DLQ logic. Do not swallow System.Exception.
  - Payment Status: Return 202 Accepted for async processing (e.g., payment confirmation).

  ---
  5. Data Model & Schema

  5.1 Core Tables

  - Ride: ID, Status, CreatedAt, RideType.
  - Order: ID, RestaurantId, RideId, Status.
  - OrderItem: MenuItemId, Quantity, Price.
  - RideView: Read Model (denormalized). Contains driver_id, location, status.
  - RideOutbox: For event persistence.

  5.2 Constraints

  - RideView is updated asynchronously via Kafka.
  - Redis GEO stores driver locations with 10s TTL.
  - Payment entities support optimistic concurrency via UpdateTimestamp.

  ---
  6. Observability

  6.1 Logging Standards

  - Format: Structured JSON logs (Serilog).
  - Fields: Always include TraceId, SpanId, Level, Timestamp, RegionId, RideId, OrderId.
  - Levels: Error for failures, Warning for retries, Info for lifecycle.

  6.2 Tracing

  - Use OpenTelemetry.
  - Propagate trace_id across Kafka headers, DB connection strings, and SignalR connections.
  - Monitor request_count, request_duration_ms, status_code.

  6.3 Health Checks

  - Implement /health, /health/readiness.
  - Readiness check depends on: Redis connection + Kafka consumer active status.

  ---
  7. Security & Auth

  - Authentication: JWT tokens in Authorization header.
  - Validation: Validate scope (e.g., rides.read) on claims.
  - Data Protection: Encrypt PII (driver names, phone numbers) in transit and at rest.

  ---
  8. Common Tasks & Do's/Don'ts

  ✅ DO

  - Use DbContext for transactions. Wrap critical operations in using (var transaction = await
  context.Database.BeginTransactionAsync()).
  - Implement IOutboxPublisher interface for event publishing.
  - Use MediatR for CQRS commands/queries if applicable (e.g., RideCommand).
  - Handle 409 errors gracefully with specific error codes (RESOURCE_CONFLICTED).

  ❌ DON'T

  - Do not bypass EF Core with direct SQL unless migrating schema.
  - Do not store sensitive data in ride_views (read model).
  - Do not sync drivers immediately from Redis GEO; use SignalR updates.
  - Do not ignore CancellationToken in async methods (especially for background jobs).

  ---
  9. Specific Logic Guidelines

  9.1 Ride Request Flow

  1. Check RideView for driver availability (Redis GEO check).
  2. If no driver, create Ride in DB.
  3. Update RideView via Kafka consumer.
  4. Return 202 for ride creation to handle async updates.

  9.2 Payment Handling

  1. Validate payment status asynchronously.
  2. If payment fails, publish PaymentFailed event to Kafka.
  3. Check webhook for retryable status changes.

  9.3 SignalR Updates

  1. Use groupIds based on rideId for real-time updates.
  2. Throttle updates to avoid flooding clients (e.g., only update on state change, not heartbeat).

  ---
  10. Performance Optimization

  - Indexing: Ensure RideId, DriverId, Status are indexed in RideView.
  - Redis: Use pipeline calls for bulk driver lookup (Geo).
  - Kafka: Batch writes for Order and OrderItem events (max 5ms latency).
  - Pooling: Use Microsoft.Data.SqlClient connection pooling settings.

  ---
  11. Testing & Deployment

  - Unit Tests: Mock Kafka/Redis. Use Moq.
  - Integration Tests: Spin up containers (Testcontainers) for Postgres/Redis/Kafka.
  - CI/CD: Build -> Test -> Deploy to Staging -> Run Smoke Tests.
  - Environments: Dev, Stage, Prod. Use AppSettings.json or UserSecrets for config.

  ---
  12. Maintenance & Migration

  - Schema Changes: Follow Flyway/EF Core migration steps.
  - Data Archiving: Move completed rides to Archive table after 90 days.
  - Cleanup: Prune RideView stale rows (TTL > 10s) nightly.
  - Metrics: Monitor KafkaConsumerFailures, PaymentTimeouts.

  ---
  13. Summary Checklist for AI Agents

  When asked to implement a feature:
  1. Check Versioning: Does the endpoint match /v1/?
  2. Check DB: Is RideView or Order involved? Is it read or write?
  3. Check Resilience: Is a fallback defined? Is Kafka retry configured?
  4. Check Observability: Are TraceId and specific RideId added to logs?
  5. Check Concurrency: Is version logic in place for updates?
  6. Check Redis: Are Geo TTLs and heartbeat checks considered?