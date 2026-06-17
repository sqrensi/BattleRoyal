# Queue Service (Node.js)

Minimal local Queue + Matchmaker service for day 3-4 MVP flow.

Persistent player data (skins, currency, nicknames, future achievements/rewards) is stored in **SQLite** (`data/shooterprototype.db` by default).

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
- `DATABASE_PATH` (default `Backend/QueueService/data/shooterprototype.db`)
