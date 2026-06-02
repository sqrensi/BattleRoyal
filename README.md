# BattleRoyal (ShooterPrototype)

Multiplayer third-person / first-person shooter prototype (Unity + Node.js queue/realtime backend).

## Requirements

- **Unity 6000.3.10f1** (Unity 6.3.10) — see `ProjectSettings/ProjectVersion.txt`
- Node.js 18+ (for `Backend/QueueService`)

## Open in Unity

1. Clone this repository.
2. Open the project folder in Unity Hub with **6000.3.10f1**.
3. Open `Assets/Scenes/MainMenu.unity` or `Assets/Scenes/Game.unity`.

### Local-only assets (not in git)

These folders are excluded via `.gitignore` (too large or third-party):

- `Library/`, `Temp/`, `Build/`, `Logs/`, `UserSettings/` — Unity cache (rebuilt locally)
- `Assets/Synty/` — Synty POLYGON pack; copy from your local install if missing
- `Assets/Prefabs/tensentmask/` — optional local mask assets

After clone, Unity reimports `Assets/` on first open.

## Backend

```bash
cd Backend/QueueService
npm install
npm start
```

Local runtime config samples: `.runtime/queue-service.json`, `.runtime/dedicated-server.json` (optional, not required for git clone).

## Repository

- GitHub: https://github.com/sqrensi/BattleRoyal
