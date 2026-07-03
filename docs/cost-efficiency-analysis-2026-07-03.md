# Gruuber — Running-Cost & Efficiency Analysis (2026-07-03)

Question: should we replace SignalR with raw WebSockets to cut costs — and where else can the app run cheaper and more efficiently?

Companion docs: [deep-analysis-2026-07-03.md](deep-analysis-2026-07-03.md), [mobile-app-analysis-2026-07-03.md](mobile-app-analysis-2026-07-03.md).

---

## 1. WebSockets vs SignalR — the direct answer

**Switching to raw WebSockets would not reduce your running costs; it would increase your engineering costs.**

- Self-hosted SignalR (what this app does — `AddSignalR()` inside the API process) is just a library over the same Kestrel WebSocket connections raw WS would use. Same sockets, same memory per connection, same scale-out problem. **Infra delta: $0.**
- What you'd have to rebuild by hand with raw WS: per-ride group fan-out, auth integration, keepalive/reconnect protocol, message framing, and client libraries for three platforms. That's weeks of work to arrive at a worse SignalR.
- The scenario where SignalR costs real money is **Azure SignalR Service** (priced per unit / 1,000 connections). The cheap answer is simply *don't adopt it*: keep self-hosting, and when you scale past one API instance, add the Redis backplane **on the Redis instance you already run** (marginal cost ≈ 0) plus sticky sessions at the load balancer. Managed SignalR only becomes worth discussing at tens of thousands of concurrent connections.
- If bandwidth is the concern, the wins inside SignalR are bigger than a transport swap: **MessagePack protocol** (30–50% smaller frames than JSON), longer keepalive intervals, and — most of all — **sending fewer messages** (see §2, Tier 2). Raw WebSockets gives you none of that for free.

**Verdict: keep SignalR, self-hosted.** The real-time layer is not where this app's money goes.

---

## 2. Where the money actually goes — ranked

### Tier 1 — Infrastructure footprint (the biggest line items)

**2.1 Kafka + Zookeeper are your two most expensive containers.** For a single-region modular monolith doing ~120 ride requests/sec at peak, a full Kafka deployment is heavily oversized. Options, in increasing savings:

| Option | Change | Saves |
|---|---|---|
| a. **Kafka KRaft mode** (recommended now) | Drop the Zookeeper container; single-broker KRaft node in docker-compose | 1 container, ~1–2 GB RAM |
| b. **Redis Streams transport** (recommended if the bill matters more than Kafka parity) | Swap the implementation behind the existing `IKafkaProducer` abstraction to Redis Streams (consumer groups supported); keep the outbox for durability/replay | Kafka *and* Zookeeper — 2 containers, the JVM's memory, and its ops burden |
| c. Managed Kafka (Confluent/MSK) | Outsource ops | Costs more cash, saves ops time — only if a microservice split is imminent |

The `IKafkaProducer`/outbox abstraction means option (b) is a contained infrastructure swap, not a rewrite. Revisit real Kafka when the microservice split actually happens.

**2.2 Postgres/Redis are already shared efficiently** — all modules use one `Default` connection string and one multiplexer. Add `AddDbContextPool` (context reuse) and an explicit `Maximum Pool Size` so seven DbContexts don't oversubscribe connections.

### Tier 2 — Chattiness (CPU, bandwidth, and log ingestion — the sneaky per-GB costs)

**2.3 Driver heartbeats: every 2–3s, unconditionally.** That's ~30,000 requests/driver/day, each one hitting the rate-limiter Lua script, Redis GEO write, sorted-set write, and an Info log line. **Adaptive cadence** — 2–3s only during an active trip, 10–15s while idle-online, nothing while offline — cuts tracking traffic (and everything downstream of it) by **70–85%**. This is the single highest-leverage efficiency change in the codebase, and it's mostly a mobile-client policy plus a documented contract.

**2.4 Hot-path Info logging.** `UpdateDriverLocationHandler` logs every heartbeat, `KafkaProducer` logs every publish, and `UseSerilogRequestLogging` logs every request including heartbeats and health probes. Log ingestion/retention is priced per GB and this multiplies traffic × 2–3 lines. Fixes: demote per-heartbeat/per-publish logs to `Debug`, exclude `/v1/drivers/location` and `/health*` from request logging, keep `Warning`+ for retries per CLAUDE.md. Near-zero effort, immediate savings.

**2.5 Per-heartbeat SignalR broadcast.** `SignalRLocationBroadcaster` forwards every heartbeat to the ride group. Throttle to max one update per 3–5s per ride (or on meaningful movement) — CLAUDE.md §9.3 already mandates this. Combined with MessagePack this cuts real-time bandwidth by well over half.

**2.6 OpenTelemetry console exporter.** Every span is serialized to stdout — pure CPU burn that also doubles your log volume (spans get ingested as log lines). Replace with an OTLP exporter honoring the existing `OpenTelemetry:Endpoint` setting, use parent-based sampling (~10% in prod), and drop the console exporter outside Development.

### Tier 3 — Constant background churn (costs even at zero traffic)

**2.7 `OutboxWorker` polls 3 DbContexts every 500 ms** — ~6 queries/sec forever, traffic or not. Fixes: collapse the three copy-pasted loops into one generic method; adaptive interval (500 ms while draining → 5 s when idle); add a partial index `WHERE status = 'pending'` on each outbox table; **prune processed rows** (currently unbounded growth = storage cost + ever-slower scans). Postgres `LISTEN/NOTIFY` can replace polling entirely if you want to go further.

**2.8 `PaymentTimeoutWorker` queries every 30s for a status nothing ever sets** (deep-analysis §5 — `PendingConfirmation` is unreachable). Until that saga is fixed this worker is pure waste; and even after, a 15-minute timeout does not need 30-second precision — poll every 5 minutes.

**2.9 Pool sweeps run every 30s even in regions with no pool rides.** Gate `PoolMatcherService`/`PoolTimeoutWorker` on an O(1) Redis `ZCARD` of the pool queue before doing DB work.

**2.10 Surge pricing runs 2 DB `COUNT` queries per ride/order creation.** The config is cached but the demand/supply counts are not. Cache the resolved multiplier per `(region, type)` for 15–30s in Redis, or adopt the spec's original Redis-counter design. At 120 requests/sec this removes ~240 write-DB queries/sec at peak.

### Tier 4 — Hygiene (small individually, free to adopt)

- `AsNoTracking()` on all read handlers (`GetRideStatusHandler`, `GetOrderHandler`, dashboards) — less EF memory/CPU per query.
- Indexes to add alongside the outbox ones: `rides (region_id, status)` (serves surge counts and pool sweeps), `ride_views (driver_id)`, `ride_views (status)` per CLAUDE.md §10.
- Response compression for JSON APIs; MessagePack for SignalR.
- Remove dead weight: the unused MediatR package in 7 csproj files (unless Phase 5 wires it), unused Testcontainers references if stubs stay skipped — smaller images, faster cold starts. Publish with `PublishReadyToRun` and the alpine runtime image.
- Kafka consumers: 4 separate consumer groups in one process is acceptable; consolidate only if broker connection count ever matters.

---

## 3. What *not* to do in the name of cost

1. **Don't replace SignalR with raw WebSockets** (§1) — cost-neutral infra, cost-negative engineering.
2. **Don't split into microservices early.** Every extracted service is its own container, connection pool, log stream, and health surface. The modular monolith is the cheap architecture; keep it until scale forces the split.
3. **Don't buy managed real-time (Azure SignalR) or managed Kafka preemptively.** Both have genuine trigger points (tens of thousands of connections; multi-region event volume) that this app is nowhere near.
4. **Don't drop the outbox pattern to save DB writes** — it's the durability backbone; prune it instead (§2.7).

---

## 4. Suggested order of attack

| # | Action | Effort | Recurring-cost impact |
|---|---|---|---|
| 1 | Demote hot-path logs, exclude heartbeats/health from request logging | Hours | High (log ingestion is per-GB) |
| 2 | Throttle location broadcasts + MessagePack | Hours | Medium |
| 3 | OTLP + sampling, drop console span exporter | Hours | Medium |
| 4 | Adaptive driver heartbeat cadence (client policy + documented contract) | Days | **Highest** |
| 5 | Outbox: generic loop, idle backoff, partial index, pruning job | Days | Medium |
| 6 | Kafka → KRaft single node (drop Zookeeper) | Hours | Medium |
| 7 | Surge multiplier caching | Hours | Medium at peak |
| 8 | Worker gating (pool ZCARD check, payment worker interval) | Hours | Low-medium |
| 9 | Decision: Redis Streams instead of Kafka for MVP | Days | **High** (removes the heaviest infra) |
| 10 | EF hygiene (`AsNoTracking`, pooling, indexes), compression, dead packages | Days | Low each, compounding |

Items 1–3 and 6–8 are pure backend changes safe to do now. Item 4 pairs naturally with the mobile M0/M1 work. Item 9 is the one strategic decision — make it before provisioning production infrastructure, because it determines your hosting bill's largest line.
