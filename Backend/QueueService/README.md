# Queue Service (Node.js)

Minimal local Queue + Matchmaker service for day 3-4 MVP flow.

Persistent player data (skins, currency, nicknames, achievements, match stats) is stored in **SQLite** locally or **PostgreSQL** in production.

## Database

| Environment | Driver | Config |
|-------------|--------|--------|
| Local dev (default) | SQLite | `DATABASE_PATH` (default `Backend/QueueService/data/shooterprototype.db`) |
| Production | PostgreSQL | `DATABASE_URL=postgres://user:pass@host:5432/dbname` |

Optional PostgreSQL pool tuning:

- `DB_POOL_MAX` (default `20`)
- `DB_POOL_IDLE_MS` (default `30000`)
- `DB_POOL_CONNECT_MS` (default `5000`)

When `DATABASE_URL` is set, PostgreSQL is used automatically. For local PostgreSQL testing without `DATABASE_URL`, set `DB_DRIVER=postgres` and provide `DATABASE_URL`.

**Production deploy:** [DEPLOY.md](./DEPLOY.md) (Windows local + VPS). Short VPS checklist: [DEPLOY-VPS.md](./DEPLOY-VPS.md).

## Endpoints

### Matchmaking

- `POST /enqueue` body: `{ "playerId": "player-123" }`
- `POST /dequeue` body: `{ "ticketId": "..." }`
- `POST /match/leave` body: `{ "ticketId": "..." }`
- `POST /match/presence/update` body: `{ "ticketId":"...", "position":{"x":0,"y":0,"z":0}, "yaw":0 }`
- `GET /match/presence/:ticketId`
- `GET /ticket/:ticketId`
- `GET /health`

### Player profile / economy

- `POST /profile/ensure` body: `{ "playerId": "player-123" }` — create profile + starter pack
- `GET /profile/:playerId` — read profile
- `POST /profile/:playerId/purchase` body: `{ "skinId": "tshirts_002" }`
- `PUT /profile/:playerId/equipped` body: `{ "slot": "shirt", "skinId": "tshirts_001" }`
- `PUT /profile/:playerId/nickname` body: `{ "nickname": "PlayerOne" }`
- `PUT /profile/:playerId/character-model` body: `{ "selectedCharacterModel": "Hero" }`

Profile response includes:

- `currencyBalance`
- `ownedSkins[]`
- `equipped { shirt, pants, boots, gloves, face, hair }`
- `nickname`
- `achievements[]` (reserved for future use)
- `claimedRewards[]` (reserved for future use)

Ticket statuses:

- `Queued`
- `Matched`
- `Cancelled`
- `Expired`

When matched, response includes `serverAddress` and `serverPort` for Unity client connect.

## Run (PowerShell)

```powershell
cd "c:\me\unity\ShooterPrototype\Backend\QueueService"
npm install
npm start
```

Optional env vars:

- `PORT` (default `5050`)
- `MATCH_SERVER_ADDRESS` (default `127.0.0.1`)
- `MATCH_SERVER_PORT` (default `7777`)
- `MIN_PLAYERS_TO_MATCH` (default `1`)
- `MATCH_TIMEOUT_SECONDS` (default `20`)
- `MATCH_BATCH_WINDOW_SECONDS` (default `2`) - waits briefly to group near-simultaneous joins into one match
- `DATABASE_PATH` (default `Backend/QueueService/data/shooterprototype.db`) — SQLite only
- `DATABASE_URL` — PostgreSQL connection string (enables production DB)
- `DB_DRIVER` (`sqlite` or `postgres`, default `sqlite` when `DATABASE_URL` is unset)
- `DB_POOL_MAX`, `DB_POOL_IDLE_MS`, `DB_POOL_CONNECT_MS` — PostgreSQL pool settings
