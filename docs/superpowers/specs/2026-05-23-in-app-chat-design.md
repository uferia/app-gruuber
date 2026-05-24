# In-App Chat Design

**Date:** 2026-05-23  
**Status:** Approved  
**Module:** Gruuber.Chat (new dedicated module)  
**Approach:** Dedicated Chat Module (B) — fully decoupled from Rides/Orders, Kafka-driven thread creation, SignalR delivery via dedicated ChatHub

---

## Overview

`Gruuber.Chat` is a standalone module providing real-time in-app messaging across three channels: Rider↔Driver, Customer↔Restaurant, and Driver↔Restaurant. It owns all chat entities and has no runtime dependencies on `Gruuber.Rides` or `Gruuber.Orders` tables — integration is event-driven via Kafka.

Messages are delivered in real-time over a dedicated `ChatHub` (SignalR), persisted to PostgreSQL, and available as read-only history for 6 hours after a ride/order completes. Real identities are never exposed — participants are always shown anonymized display names.

---

## Constraints & Decisions

| Decision | Choice |
|---|---|
| Architecture | Dedicated `Gruuber.Chat` module — no FK to rides/orders tables |
| Channels | Rider↔Driver, Customer↔Restaurant, Driver↔Restaurant |
| Message types | Text + quick reply templates |
| Thread lifetime | Active during ride/order + read-only for 6h after completion |
| Read receipts | Single tick (sent) → double tick (read) |
| Identity masking | Real names/phones never exposed — display names are role-based ("Driver", "Rider") |
| Moderation | None at launch |
| Real-time transport | SignalR `ChatHub` (separate from `TrackingHub`), Redis backplane for multi-instance fan-out |

---

## Data Model

### `chat_threads`

```sql
CREATE TABLE chat_threads (
  id           UUID PRIMARY KEY,
  context_id   UUID NOT NULL,    -- ride_id or order_id (no FK — decoupled)
  context_type TEXT NOT NULL,    -- 'ride' | 'order'
  channel      TEXT NOT NULL,    -- 'rider_driver' | 'customer_restaurant' | 'driver_restaurant'
  status       TEXT NOT NULL DEFAULT 'active',  -- 'active' | 'read_only' | 'closed'
  closes_at    TIMESTAMP,        -- set to completed_at + 6h on ride/order completion
  region_id    INT NOT NULL,
  created_at   TIMESTAMP NOT NULL DEFAULT now()
);
```

### `chat_participants`

```sql
CREATE TABLE chat_participants (
  thread_id    UUID NOT NULL REFERENCES chat_threads(id),
  user_id      UUID NOT NULL,
  role         TEXT NOT NULL,    -- 'rider' | 'driver' | 'restaurant'
  display_name TEXT NOT NULL,    -- anonymized: "Driver", "Rider", "Restaurant"
  joined_at    TIMESTAMP NOT NULL DEFAULT now(),
  PRIMARY KEY (thread_id, user_id)
);
```

### `chat_messages`

```sql
CREATE TABLE chat_messages (
  id           UUID PRIMARY KEY,
  thread_id    UUID NOT NULL REFERENCES chat_threads(id),
  sender_id    UUID NOT NULL,
  message_type TEXT NOT NULL,    -- 'text' | 'quick_reply'
  content      TEXT NOT NULL,    -- message body or quick_reply key
  status       TEXT NOT NULL DEFAULT 'sent',  -- 'sent' | 'delivered' | 'read'
  sent_at      TIMESTAMP NOT NULL DEFAULT now(),
  read_at      TIMESTAMP
);

CREATE INDEX ON chat_messages (thread_id, sent_at);
```

### `quick_reply_templates`

```sql
CREATE TABLE quick_reply_templates (
  key              TEXT PRIMARY KEY,     -- e.g. 'im_outside'
  display_text     TEXT NOT NULL,        -- "I'm outside"
  applicable_roles TEXT[],              -- ['driver', 'rider', 'restaurant']
  is_active        BOOLEAN NOT NULL DEFAULT true
);
```

---

## Thread Lifecycle

### Thread Creation (Kafka-driven)

`ChatThreadConsumer` listens to domain events from all region-scoped topics:

| Kafka Event | Action |
|---|---|
| `ride_matched` | Create thread: `context=rideId`, `channel='rider_driver'`; add rider + driver as participants |
| `order_accepted` | Create 2 threads: `channel='customer_restaurant'` and `channel='driver_restaurant'`; add relevant participants |
| `ride_completed` / `order_delivered` | Set `thread.closes_at = now() + 6h`; emit `chat_thread_closing` via SignalR to participants |

A scheduled sweep job runs every 5 minutes and marks threads past `closes_at` as `read_only`.

### Thread State Machine

```
active → read_only → closed
```

- **`active`** — messages can be sent and received
- **`read_only`** — history accessible, new messages rejected
- **`closed`** — fully archived (future cleanup job)

---

## SignalR — ChatHub

`ChatHub` is dedicated to `Gruuber.Chat` and separate from `TrackingHub`. It uses the same Redis backplane for multi-instance fan-out.

### Connection & Join

```
Client → JoinThread(threadId)
  → Validate JWT: caller must exist in chat_participants for this thread (else 403)
  → Add to SignalR group: chat:{threadId}
  → Mark any unread messages as 'delivered' for this user
```

### Send Message

```
Client → SendMessage(threadId, content, messageType)
  → Validate thread.status == 'active' (else error: THREAD_CLOSED)
  → Persist to chat_messages (status='sent')
  → Broadcast to group chat:{threadId}: ReceiveMessage(message)
  → Update status='delivered' for currently connected recipients
```

### Read Receipt

```
Client → MarkRead(messageId)
  → UPDATE chat_messages SET status='read', read_at=now()
  → Notify sender via SignalR: MessageRead(messageId)
```

Single tick = `sent`. Double tick = `read`.

---

## REST API

All endpoints require a valid JWT. Users can only access threads they participate in.

```
GET /v1/chat/threads?context_id={rideOrOrderId}
  → 200 { threads: [{ id, channel, status, closes_at }] }
  Caller's threads for this context only.

GET /v1/chat/threads/{threadId}/messages?before={cursor}&limit=20
  → 200 { items: [...], next_cursor }
  Cursor-based pagination, sorted by sent_at DESC.
  Available for both active and read_only threads.

GET /v1/chat/quick-replies?role=driver|rider|restaurant
  → 200 { templates: [{ key, display_text }] }
  Filtered to caller's role. Active templates only.
```

---

## Kafka Events Consumed

| Event | Source Topic | Action |
|---|---|---|
| `ride_matched` | `ride-events-{region}` | Create rider↔driver thread |
| `order_accepted` | `order-events-{region}` | Create customer↔restaurant + driver↔restaurant threads |
| `ride_completed` | `ride-events-{region}` | Set closes_at on rider↔driver thread |
| `order_delivered` | `order-events-{region}` | Set closes_at on order threads |

DLQ: `chat-events-dlq-{region}` — after 5 failed retries with exponential backoff + jitter.

---

## Error Handling

| Scenario | Handling |
|---|---|
| Message sent to `read_only` or `closed` thread | SignalR error to sender: `THREAD_CLOSED` |
| User not in `chat_participants` tries to join | Hub rejects with `403 Forbidden` |
| Kafka lag — thread not yet created when client connects | Client retries `GET /v1/chat/threads?context_id=` with backoff (max 5s) |
| SignalR client disconnects mid-conversation | Messages persist in DB at `sent`; delivered via history endpoint on reconnect |
| Kafka consumer fails to create thread | Retry max 5 times; route to `chat-events-dlq-{region}` |
| Invalid quick reply key | `400 Bad Request` |

---

## Observability

### Log Fields

All chat log entries include: `TraceId`, `SpanId`, `RegionId`, `ThreadId`, `ContextId`, `ContextType`, `Channel`.

| Level | Events |
|---|---|
| Info | Thread created, message sent, thread closed |
| Warning | Thread creation retried, consumer lag elevated |
| Error | Thread creation DLQ'd, unauthorized join attempt |

### Metrics

| Metric | Purpose |
|---|---|
| `chat_messages_per_trip` | Avg messages per active thread — engagement signal |
| `chat_thread_creation_lag_ms` | Time from Kafka event → thread available; alert if > 2s |
| `chat_signalr_connections` | Active ChatHub connections gauge |
| `chat_dlq_events` | Failed thread creation events routed to DLQ |

---

## Testing

### Unit Tests (Moq)

- Thread created from `ride_matched` → correct participants, channel, and status
- Thread created from `order_accepted` → exactly 2 threads with correct channels
- Message to `read_only` thread → rejected with `THREAD_CLOSED`
- `MarkRead` → `status='read'`, `read_at` set, sender receives `MessageRead` notification
- Quick reply with invalid key → `400 Bad Request`

### Integration Tests (Testcontainers — Postgres + Kafka + SignalR)

- End-to-end: `ride_matched` event → thread created → rider sends message → driver receives via SignalR
- Read receipt: driver reads message → sender gets double-tick `MessageRead` notification
- Thread closes 6h after completion → subsequent message send rejected with `THREAD_CLOSED`
- Client disconnects and reconnects → history endpoint returns missed messages in correct order
- Consumer fails 5 times → event lands in `chat-events-dlq-{region}`

### Privacy Invariants

- Assert: `chat_participants.display_name` never contains real name, phone number, or email
- Assert: user absent from `chat_participants` cannot join thread or read its messages via REST

---

## Pre-Implementation Checklist

- [ ] `/v1/chat/` versioning on all endpoints
- [ ] `ChatHub` is separate from `TrackingHub` — no shared group names
- [ ] No FK references from `chat_threads` to `rides` or `orders` tables
- [ ] `chat_participants.display_name` enforced as role-based anonymous label at write time
- [ ] Thread `status` checked before every `SendMessage` call
- [ ] Kafka consumers use `IExponentialBackoff`, max 5 retries, DLQ on failure
- [ ] `ThreadId`, `ContextId`, `Channel` in all chat log entries
- [ ] `chat_thread_creation_lag_ms` metric wired to alerting
- [ ] `CancellationToken` propagated in all async consumer and hub methods
- [ ] Redis backplane shared with existing SignalR infrastructure (no new Redis instance)
