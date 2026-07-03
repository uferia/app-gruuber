# Gruuber — Mobile App Deep Analysis (2026-07-03)

Question: what would it take to put a mobile app (or apps) on top of this solution, and is the backend ready for it?

Companion doc: [deep-analysis-2026-07-03.md](deep-analysis-2026-07-03.md) — several backend findings there are hard blockers for mobile and are cross-referenced below.

---

## 1. Verdict

The backend's *shape* is mobile-friendly — JWT access/refresh with rotation, 202-async command patterns, SignalR hubs, consistent `ApplicationResult` error envelopes, URL versioning. But a mobile client today could not build its home screen (no list endpoints), would never hear that a driver was matched (no status push), would go silent the moment it's backgrounded (no push notifications), and the driver-side matching model doesn't fit a driver app at all (no offer/accept flow). **Plan for a backend "Mobile Readiness" phase before the first screen is built.**

Three personas → recommend **two mobile apps + keep restaurant on web**:

| Persona | Client | Why |
|---|---|---|
| Rider | Mobile app #1 | Core consumer UX: request ride, order food, track, chat, pay |
| Driver | Mobile app #2 | Fundamentally different UX: background GPS, offers, trip lifecycle, earnings |
| Restaurant | Web dashboard (existing API) | Tablet-in-kitchen use case; analytics/export endpoints already suit a web UI |

---

## 2. What the backend already provides (usable as-is from mobile)

- **Auth flow**: `POST /v1/auth/register|register/rider|register/driver`, `login`, `refresh` with rotating refresh tokens (hashed, revocable) — the right model for mobile token storage (Keychain/Keystore). Access TTL 15 min is sensible.
- **Async command pattern**: ride request / order create return `202 Accepted` with `pending_match` semantics — good for optimistic mobile UX.
- **Error contract**: uniform `ErrorCode`/`ErrorMessage` + `409 RESOURCE_CONFLICTED` with entity version — clients can implement one error interceptor.
- **Chat module**: thread list, paginated history, quick replies, hub with join/send/read receipts — the most client-ready module in the codebase.
- **Driver analytics**: summary/trips/earnings-export endpoints map directly to a driver-app earnings tab.
- **Surge estimate**: `GET /v1/surge/estimate` supports a pre-booking fare preview screen.

---

## 3. Blocking backend gaps (must land before mobile MVP)

### 3.1 Security first — mobile makes the IDOR holes trivially exploitable
Everything in the companion doc's **P0 §3** (missing ownership checks on rides/orders/payments, open LocationHub/ChatHub joins) must be fixed before shipping any public client: a mobile app hands every user a bearer token and a discoverable API. Likewise **P0 §1** (rate limiter counts everyone as anonymous, 20 req/min per IP) breaks mobile outright — carrier-grade NAT puts thousands of users behind one IP, and a single driver heartbeat already saturates the bucket.

### 3.2 No way to build a home screen — missing list/identity endpoints
All ride/order/payment reads are by-ID. After an app restart there is no way to recover state. Needed:
- `GET /v1/rides?scope=active|history` (rider) and driver's current assigned ride
- `GET /v1/orders?scope=active|history`
- `GET /v1/me` (profile, role, region, driver approval status)
- `POST /v1/auth/logout` (revoke refresh token — the domain supports revocation; no endpoint exposes it)

### 3.3 No server push of lifecycle events
SignalR broadcasts **location only**. "Driver matched", "driver arrived", "order ready" — the moments a rider actually cares about — require polling `GET /v1/rides/{id}`. Needed: a Kafka consumer that fans lifecycle events out to `rideId`/`orderId` hub groups (companion doc §14).

### 3.4 No push notifications (the biggest net-new backend feature)
SignalR only works while the app is foregrounded with a live socket. Every critical event (driver matched, order status, chat message, payment result) must also reach a backgrounded/killed app. Needed:
- `POST /v1/devices` — register FCM/APNs device token per user (+ delete on logout)
- A `NotificationDispatcher` background service consuming the existing region Kafka topics and mapping events → push payloads (FCM HTTP v2 covers both platforms)
- Per-event-type opt-in/out preferences (can be deferred)
This slots cleanly into the existing consumer pattern (`AnalyticsConsumerService` is the template).

### 3.5 Driver matching doesn't fit a driver app
Today a driver calls `POST /v1/rides/{id}/match` and the system assigns the **best-scored** driver — potentially *not the caller*. A real driver app needs an **offer state machine**:
1. Matching engine (server-initiated, not driver-initiated) selects the best candidate.
2. Offer pushed to that driver (push notification + SignalR) with a TTL (~15s).
3. Driver accepts (optimistic-version transition `requested → matched`) or declines/times out → next candidate.
This is a re-work of the matching flow, not an add-on; budget it as its own backend work item.

### 3.6 Food ordering can't start — no discovery surface
There are no restaurant or menu endpoints (companion doc §8). The rider app's food tab needs `GET /v1/restaurants?region=…`, `GET /v1/restaurants/{id}/menu`, and server-side menu prices (which also closes the client-priced-order hole). Recommendation: **ship the rider app's ride vertical first; add food in a later release** once the restaurant backend vertical exists.

### 3.7 Real-time transport details
- No Redis backplane on SignalR — fine for one instance, breaks the moment the API scales out (spec already promises it; not wired).
- Hubs authenticate via `Authorization` header only. Native SignalR clients (.NET/Java/Swift/JS in native shells) can send headers, so mobile works — but add the standard `access_token` query-string hook (`OnMessageReceived`) if a web/PWA client is ever planned.
- Location broadcast is unthrottled per-heartbeat and the driver client chooses `ActiveRideId` — server should resolve the driver's active ride itself.

---

## 4. Mobile-side architecture recommendations

### 4.1 Framework
The team is a C#/.NET shop, which puts **.NET MAUI** on the table, but evaluate against what these apps actually are — maps-heavy, real-time, background-location apps:

| Option | For | Against |
|---|---|---|
| **Flutter** (recommended) | Best map/animation performance of the cross-platform trio; single codebase for both apps; strong background-geolocation plugins; `signalr_netcore` package works with ASP.NET Core hubs | New language (Dart) for a C# team — mitigated by similar syntax |
| React Native (Expo) | Huge ecosystem; good maps; OTA updates via Expo | Background location + battery tuning is the roughest of the three; JS toolchain |
| .NET MAUI | Same language as backend, share DTO contracts as a NuGet package; first-class SignalR client | Weakest maps/background-geo story; smallest talent pool/ecosystem for consumer-grade ride-hailing UX |

Recommendation: **Flutter** for both apps, sharing a core package (auth/token refresh, API client generated from Swagger, SignalR wrapper, design system). If staying all-C# outweighs UX polish for an MVP, MAUI is acceptable — but plan extra time for the driver app's background tracking.

### 4.2 Client patterns dictated by this backend
- **Token lifecycle**: interceptor that retries once on 401 via `/v1/auth/refresh`; refresh tokens in secure storage; hard logout on refresh failure. Handle driver `approvalStatus` (login already returns it) as a gate screen.
- **409 handling**: every transition needs `ExpectedVersion` — clients must track entity versions from GETs and re-fetch on `RESOURCE_CONFLICTED` (the API intentionally returns minimal data on conflict).
- **Offline/retry**: commands are POSTs over flaky networks — the backend's missing idempotency keys (companion doc P0 §4) matter doubly here; until fixed, clients must guard against double-submission themselves (disable-on-tap is not enough).
- **Tracking fallback**: live tracking = SignalR group + poll `GET /v1/rides/{id}` as fallback (the API was designed for this; keep polling ≥5s to respect rate limits).
- **Driver location cadence**: 2–3s heartbeat only while online/on-trip; use platform foreground-service (Android) / background-location mode (iOS) with distance-filter batching to save battery.
- **Region**: `region_id` is baked into the JWT at registration — the apps don't need region pickers, but changing regions requires re-auth (flag if roaming between cities is a product requirement).

### 4.3 Security posture for mobile
- Store tokens in Keychain (iOS) / EncryptedSharedPreferences-Keystore (Android); never in plain storage.
- Certificate pinning optional for MVP (mock payments); required before real payment providers.
- Chat display names are already anonymized role labels server-side — good; keep PII out of push payloads too (send "Your driver has arrived", not names, per CLAUDE.md PII policy).

---

## 5. Phased roadmap

**Phase M0 — Backend mobile-readiness (blocks everything)**
Deep-analysis Phases 1–2 (security + sagas) plus: list endpoints, `GET /v1/me`, logout, lifecycle status push over SignalR, push-notification pipeline, driver offer/accept flow, SignalR Redis backplane.

**Phase M1 — Rider app MVP (rides only)**
Auth/onboarding → request ride (with surge preview) → matching status via push/SignalR → live tracking map → trip lifecycle → mock payment → chat with driver → ride history.

**Phase M2 — Driver app**
Onboarding/approval gate → go online/offline → heartbeat → offer receive/accept/decline → trip flow (en_route/arrived/complete) → chat → earnings tab (analytics endpoints exist).

**Phase M3 — Food vertical in rider app**
After the restaurant backend vertical (deep-analysis Phase 3): discovery, menu, cart (server prices), order tracking, restaurant chat thread.

**Phase M4 — Hardening**
Push preferences, offline queues, cert pinning + real payments, load testing against the spec's latency targets (p95 < 400ms) with mobile traffic patterns.

---

## 6. Open product decisions (need answers before M1 build)

1. One combined app with role switching vs. separate rider/driver apps? (Recommended: separate — different store listings, permissions, and background-location review requirements.)
2. Is region roaming (a rider traveling to another city) a requirement? Affects JWT `region_id` design.
3. Real payment provider timeline — determines when cert pinning and PCI-adjacent choices land.
4. Minimum OS targets (affects background-location APIs: Android 14+ foreground-service types, iOS 17+ push entitlements).
