# End-to-End Testing Guide: LocationHub (SignalR Live Tracking)

This guide walks through testing the full real-time driver location flow:

```
Rider connects to SignalR → Requests a ride → Driver pushes location →
Rider receives DriverLocationUpdated via SignalR
```

---

## Prerequisites

| Dependency | Local setup |
|---|---|
| PostgreSQL | `docker run -e POSTGRES_USER=gruuber -e POSTGRES_PASSWORD=gruuber -e POSTGRES_DB=gruuber -p 5432:5432 postgres:16` |
| Redis | `docker run -p 6379:6379 redis:7` |
| Kafka | `docker run -p 9092:9092 -e KAFKA_ADVERTISED_LISTENERS=PLAINTEXT://localhost:9092 -e KAFKA_LISTENER_SECURITY_PROTOCOL_MAP=PLAINTEXT:PLAINTEXT -e KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1 apache/kafka:3.7.0` |
| .NET 8 SDK | [https://dotnet.microsoft.com/download](https://dotnet.microsoft.com/download) |

> **Tip:** All three dependencies can also be started together with a single `docker compose up` if you have a compose file in the repo root.

---

## Step 1 — Start the API

```powershell
cd C:\Projects\app-gruuber
dotnet run --project src\Gruuber.Api\Gruuber.Api.csproj --launch-profile https
```

On first startup in `Development` environment, `DevDataSeeder` automatically creates three test accounts and seeds the driver's location near Times Square (40.7580, -73.9855), region 1:

| Role | Email | Password |
|---|---|---|
| Rider | `rider@test.com` | `Password123!` |
| Driver | `driver@test.com` | `Password123!` |
| Restaurant | `restaurant@test.com` | `Password123!` |

Seeded IDs (stable across restarts):

```
RiderUserId:   aaaaaaaa-0000-0000-0000-000000000001
DriverUserId:  aaaaaaaa-0000-0000-0000-000000000002
```

---

## Step 2 — Obtain JWT Tokens

### Rider token

```http
POST https://localhost:7272/v1/auth/login
Content-Type: application/json

{
  "email": "rider@test.com",
  "password": "Password123!"
}
```

Save the `accessToken` from the response as `RIDER_TOKEN`.

### Driver token

```http
POST https://localhost:7272/v1/auth/login
Content-Type: application/json

{
  "email": "driver@test.com",
  "password": "Password123!"
}
```

Save the `accessToken` as `DRIVER_TOKEN`.

---

## Step 3 — Connect the Rider to SignalR

The hub is mounted at: `wss://localhost:7272/hubs/location`

Authentication uses the JWT as a query string parameter (standard SignalR WebSocket transport):

```
wss://localhost:7272/hubs/location?access_token=<RIDER_TOKEN>
```

### Option A — Browser DevTools console (quickest)

```javascript
// Requires @microsoft/signalr loaded from CDN
const signalR = await import("https://cdn.jsdelivr.net/npm/@microsoft/signalr@8/dist/browser/signalr.min.js");

const connection = new signalR.HubConnectionBuilder()
  .withUrl("https://localhost:7272/hubs/location", {
	accessTokenFactory: () => "<RIDER_TOKEN>"
  })
  .withAutomaticReconnect()
  .build();

connection.on("DriverLocationUpdated", (data) => {
  console.log("[LocationHub] Driver update received:", data);
});

await connection.start();
console.log("SignalR connected, connectionId:", connection.connectionId);
```

### Option B — wscat (CLI)

```powershell
npm install -g wscat
wscat -c "wss://localhost:7272/hubs/location" --header "Authorization: Bearer <RIDER_TOKEN>"
```

---

## Step 4 — Join the Ride Group

After requesting a ride (Step 5), or to pre-subscribe using a known ride ID, invoke `JoinRideGroup`:

```javascript
// In the browser console after connection.start() completes
const rideId = "<RIDE_ID>";   // from Step 5 response
await connection.invoke("JoinRideGroup", rideId);
console.log("Joined group for ride:", rideId);
```

---

## Step 5 — Request a Ride (as Rider)

```http
POST https://localhost:7272/v1/rides/request
Authorization: Bearer <RIDER_TOKEN>
Content-Type: application/json

{
  "riderId": "aaaaaaaa-0000-0000-0000-000000000001",
  "rideType": "standard",
  "pickupLat": 40.7580,
  "pickupLng": -73.9855,
  "regionId": 1
}
```

Expected response: `202 Accepted` with a body containing `rideId`.

```json
{
  "rideId": "<RIDE-UUID>",
  "status": "requested"
}
```

Copy `rideId` and use it to join the SignalR group in Step 4 (or re-invoke `JoinRideGroup` now).

---

## Step 6 — Push a Driver Location Update (as Driver)

This call triggers `UpdateDriverLocationHandler`, which calls `IGeoService` to update Redis GEO and `ILocationBroadcaster.BroadcastDriverLocationAsync`, which pushes to the SignalR group.

```http
POST https://localhost:7272/v1/drivers/location
Authorization: Bearer <DRIVER_TOKEN>
Content-Type: application/json

{
  "driverId": "aaaaaaaa-0000-0000-0000-000000000002",
  "lat": 40.7590,
  "lng": -73.9845,
  "regionId": 1,
  "activeRideId": "<RIDE-UUID>"
}
```

Expected response: `200 OK`.

---

## Step 7 — Verify the SignalR Push

Back in the browser console (or wscat output) you should see:

```json
{
  "driverId": "aaaaaaaa-0000-0000-0000-000000000002",
  "lat": 40.7590,
  "lng": -73.9845,
  "timestamp": "2025-01-01T12:00:00.000Z"
}
```

The event name is `DriverLocationUpdated`. This confirms the full path:

```
POST /v1/drivers/location
  → UpdateDriverLocationHandler
	→ RedisGeoService (updates driver_locations:{regionId} GEO key with 10s TTL)
	→ SignalRLocationBroadcaster
	  → IHubContext<LocationHub>.Clients.Group(rideId)
		→ DriverLocationUpdated pushed to rider client
```

---

## Step 8 — Leave the Ride Group

```javascript
await connection.invoke("LeaveRideGroup", rideId);
```

Subsequent driver location pushes for that ride will no longer be received.

---

## Step 9 — Poll Ride Status (optional)

As a fallback when SignalR is unavailable, clients can poll:

```http
GET https://localhost:7272/v1/rides/<RIDE-UUID>
Authorization: Bearer <RIDER_TOKEN>
```

---

## Verifying Infrastructure

### Health endpoints

```http
GET https://localhost:7272/health          # liveness (always responds)
GET https://localhost:7272/health/readiness # requires Redis + Postgres to be healthy
```

### Swagger UI

```
https://localhost:7272/swagger
```

Click **Authorize**, paste `<RIDER_TOKEN>` or `<DRIVER_TOKEN>`, then invoke any endpoint interactively.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `502` / connection refused on startup | Postgres/Redis/Kafka not running | Start Docker containers (Step 0) |
| `401 Unauthorized` on `/v1/drivers/location` | Missing or expired JWT | Re-login and use the new `accessToken` |
| SignalR connects but no messages arrive | `JoinRideGroup` not called, or wrong `rideId` | Confirm `rideId` matches the UUID in the driver POST body `activeRideId` |
| `DriverLocationUpdated` not fired | `activeRideId` is null in driver POST | Include `activeRideId` — broadcast is skipped when it is null |
| Ride stays in `requested` status | Driver not seeded in Redis GEO | Restart API in `Development` so `DevDataSeeder` re-seeds driver location |
| JWT validation fails after restart | Signing key regenerated | Re-login to get fresh tokens |

---

## Quick Reference: Fixed Dev IDs

```
RiderUserId:      aaaaaaaa-0000-0000-0000-000000000001
DriverUserId:     aaaaaaaa-0000-0000-0000-000000000002
RestaurantUserId: aaaaaaaa-0000-0000-0000-000000000003
RestaurantId:     bbbbbbbb-0000-0000-0000-000000000001
RegionId:         1
Driver seed location: 40.7580, -73.9855 (Times Square, NYC)
```
