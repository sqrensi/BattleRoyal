#!/usr/bin/env node
"use strict";

const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const outFile = path.join(root, "DUEL_SERVER_ARCHITECTURE_AND_CODE.txt");

const EXCLUDE_PATH_PARTS = [
  "node_modules",
  ".git",
  "DUEL_SERVER_ARCHITECTURE_AND_CODE.txt",
  "tools/export-duel-architecture.js",
  "tools/refactor-server-duel-only.js",
];

const EXCLUDE_FILE_NAMES = [
  "package-lock.json",
  "server.legacy.js",
];

const INCLUDE_DIRS = ["workers", "network", "bots", "db", "lib", "data", "tools"];

const ROOT_FILES = [
  "server.js",
  "config.js",
  "package.json",
  "README.md",
  "DEPLOY.md",
  "DEPLOY-VPS.md",
  "env.vps.example",
  "env.windows.example",
  "YANDEX-GAMES-HTTPS-CSP.md",
];

function shouldExclude(relPath) {
  const normalized = relPath.replace(/\\/g, "/");
  if (EXCLUDE_FILE_NAMES.includes(path.basename(normalized))) {
    return true;
  }
  return EXCLUDE_PATH_PARTS.some((part) => normalized.includes(part));
}

function walkDir(absDir, relPrefix, out) {
  if (!fs.existsSync(absDir)) {
    return;
  }

  const entries = fs.readdirSync(absDir, { withFileTypes: true })
    .sort((a, b) => a.name.localeCompare(b.name));

  for (const entry of entries) {
    const rel = relPrefix ? `${relPrefix}/${entry.name}` : entry.name;
    if (shouldExclude(rel)) {
      continue;
    }

    const abs = path.join(absDir, entry.name);
    if (entry.isDirectory()) {
      out.tree.push(`${rel}/`);
      walkDir(abs, rel, out);
      continue;
    }

    if (/\.(js|json|sql|md|example)$/i.test(entry.name)) {
      out.tree.push(rel);
      out.files.push(rel);
    }
  }
}

function readText(rel) {
  const fp = path.join(root, rel);
  return fs.existsSync(fp) ? fs.readFileSync(fp, "utf8") : null;
}

function parseTopLevelFunctions(source, prefix) {
  const rows = [];
  const patterns = [
    /^function\s+([A-Za-z0-9_]+)/gm,
    /^async function\s+([A-Za-z0-9_]+)/gm,
    /^class\s+([A-Za-z0-9_]+)/gm,
  ];
  for (const pattern of patterns) {
    for (const match of source.matchAll(pattern)) {
      rows.push({
        name: `${prefix}${match[1]}`,
        line: source.slice(0, match.index).split("\n").length,
      });
    }
  }
  return rows.sort((a, b) => a.line - b.line);
}

function buildArchitectureOverview() {
  const lines = [];
  lines.push("ARCHITECTURE — DUEL 1v1 (worker-thread model)");
  lines.push("=".repeat(72));
  lines.push("");
  lines.push("TARGET: 100 concurrent human players (50 duel matches), ping < 70 ms");
  lines.push("HARDWARE REFERENCE: VPS 6 vCPU / 12 GB RAM");
  lines.push("");
  lines.push("STACK");
  lines.push("  Node.js 18+");
  lines.push("  HTTP  :5050  — matchmaking, profiles, admin bots");
  lines.push("  WS    :5051  — realtime poses, shots, snapshots, ping");
  lines.push("  DB    PostgreSQL (prod) / SQLite (local)");
  lines.push("  worker_threads — isolated match simulation");
  lines.push("");
  lines.push("PROCESS MODEL");
  lines.push("  ┌─────────────────────────────────────────────────────────────┐");
  lines.push("  │ MASTER (server.js) — single Node event loop                │");
  lines.push("  │  • HTTP /enqueue /ticket /profile /health /admin/bots/*    │");
  lines.push("  │  • WebSocket accept + ping/pong (fast path, no worker)      │");
  lines.push("  │  • Route pose/shot/hit → worker via postMessage             │");
  lines.push("  │  • Relay SNAPSHOT / MATCH_STATE / PLAYER_MESSAGE → clients   │");
  lines.push("  └───────────────┬─────────────────────────────────────────────┘");
  lines.push("                  │ postMessage (PLAYER_EVENT, CREATE_MATCH, …)");
  lines.push("      ┌───────────┼───────────┬───────────┐");
  lines.push("      ▼           ▼           ▼           ▼");
  lines.push("  Worker 0    Worker 1    Worker 2    Worker 3   (gameWorker.js)");
  lines.push("  ~12 matches ~12 matches ~13 matches ~13 matches");
  lines.push("  Each worker: Duel instances, 30 Hz tick, 20 Hz snapshots (default)");
  lines.push("");
  lines.push("DIRECTORY STRUCTURE");
  lines.push("  server.js              Master process entry + snapshot delivery queue");
  lines.push("  config.js              Env tunables");
  lines.push("  workers/");
  lines.push("    manager.js           Worker pool, match assignment");
  lines.push("    gameWorker.js        Tick loop, snapshot broadcast, pending JOIN flush");
  lines.push("    duel.js              Duel phases, rounds, combat hooks");
  lines.push("    player.js            Per-player state");
  lines.push("    combat.js            Weapon equip, hits, damage");
  lines.push("    movement.js          Pose apply (presence + fight), movement tick");
  lines.push("    clientProtocol.js    Unity snapshot + match_state JSON builders");
  lines.push("    snapshot.js            Binary/JSON snapshot encode wrapper");
  lines.push("    protocol.js            Master ↔ Worker message types");
  lines.push("    spawnTable.js        Duel spawn positions (1x1 map)");
  lines.push("  network/");
  lines.push("    websocket.js         WS server :5051, rate limits, outbound gate");
  lines.push("    snapshotBinary.js    RTS1 v12 binary snapshots (Unity RealtimeSnapshotBinaryCodec)");
  lines.push("    poseBinary.js        Decode client binary poses (RTP1)");
  lines.push("    packets.js           Legacy SSAP binary (not used by Unity)");
  lines.push("  lib/");
  lines.push("    wsOutbound.js        WS backpressure (soft/hard bufferedAmount limits)");
  lines.push("    rateLimiter.js       Per-ticket pose/shot/hit rate limits");
  lines.push("  tools/");
  lines.push("    verify-snapshot-binary.js  Startup codec self-test (also in server.js)");
  lines.push("  bots/");
  lines.push("    botManager.js        Load-test WS bots (/admin/bots/*)");
  lines.push("    duelBot.js           Bot client: enqueue + WS + pose");
  lines.push("  db/                    Profiles, economy, leaderboard");
  lines.push("  data/                  Shop/achievement/skin catalogs");
  lines.push("");
  lines.push("LEGACY (not in runtime path, kept in repo for reference only)");
  lines.push("  server.legacy.js       Monolithic pre-worker server");
  lines.push("  duelMatch.js           Old in-process duel state (replaced by workers/duel.js)");
  lines.push("");
  lines.push("MATCHMAKING FLOW");
  lines.push("  1. Client POST /enqueue { playerId, matchMode: \"duel\" }");
  lines.push("     → ensurePlayerForQueue (fast) + ticket in queue");
  lines.push("  2. tryMatchQueue scans queue for first compatible pair (human↔human, bot↔bot)");
  lines.push("  3. makeMatch assigns worker, sets ticket Matched");
  lines.push("  4. Client polls GET /ticket/:id until status=Matched");
  lines.push("  5. Client WS connect → { type:\"join\", ticketId } (after HTTP health OK)");
  lines.push("  6. Worker CREATE_MATCH → flush pending JOINs → match_state");
  lines.push("  7. Worker onJoin sets connected=true + spawn pose; both connected → prep → round_pick → fight");
  lines.push("");
  lines.push("SNAPSHOT / REMOTE PRESENCE (server ↔ Unity MatchPresenceSync)");
  lines.push("  • buildSnapshotForViewer: duel always includes opponent (spawn pose before client pose)");
  lines.push("  • Binary RTS1 v12 via network/snapshotBinary.js (USE_BINARY_SNAPSHOTS=1 default)");
  lines.push("  • Field order must match Assets/.../RealtimeSnapshotBinaryCodec.cs");
  lines.push("  • Master queues latest snapshot per ticket if WS not bound yet; flush on join");
  lines.push("  • Snapshot WS priority NORMAL (not LOW); backpressure via lib/wsOutbound.js");
  lines.push("  • Unity MatchPresenceSync: entity interpolation, movement signature dedup, no 4m snap");
  lines.push("  • Client waits WS ready before scene load (MainMenuController.ConnectAndEnterGameRoutine)");
  lines.push("");
  lines.push("UNITY CLIENT (remote presence + duel, not bundled below)");
  lines.push("  Assets/Scripts/Network/RealtimeTransportClient.cs   WS connect, snapshot decode");
  lines.push("  Assets/Scripts/Network/RealtimeSnapshotBinaryCodec.cs  RTS1 decoder (must match snapshotBinary.js)");
  lines.push("  Assets/Scripts/Player/MatchPresenceSync.cs          Remote avatars, interpolation, pose send");
  lines.push("  Assets/Scripts/Player/MatchDuelController.cs        Duel phases, spawn teleport, HUD");
  lines.push("  Assets/Scripts/Player/PlayerSpawnManager.cs         Attach MatchPresenceSync on spawn");
  lines.push("  Assets/Scripts/UI/MainMenuController.cs             Queue → WS ready → load 1x1 scene");
  lines.push("  Assets/Scripts/Bootstrap/GameBootstrap.cs             DontDestroyOnLoad launcher + transport");
  lines.push("");
  lines.push("DUEL PHASES (workers/duel.js → clientProtocol mapPhaseToClient)");
  lines.push("  waiting      → lobby     (wait both WS joins)");
  lines.push("  prep         → prep      (15 s warmup)");
  lines.push("  weapon_pick  → round_pick");
  lines.push("  fight        → round     (60 s combat)");
  lines.push("  round_end    → round_end");
  lines.push("  match_end    → ending");
  lines.push("");
  lines.push("HTTP ENDPOINTS");
  lines.push("  GET  /health");
  lines.push("  POST /enqueue");
  lines.push("  POST /dequeue");
  lines.push("  GET  /ticket/:ticketId");
  lines.push("  POST /match/:matchId/leave");
  lines.push("  GET/POST /profile/*");
  lines.push("  POST /admin/bots/spawn   { count }   (BOTS_ENABLED=1)");
  lines.push("  POST /admin/bots/stop");
  lines.push("  GET  /admin/bots/status");
  lines.push("");
  lines.push("WEBSOCKET MESSAGES (client → server)");
  lines.push("  join, pose (binary RTP1 or JSON), shot, hit");
  lines.push("  duel_weapon_pick, ping");
  lines.push("");
  lines.push("WEBSOCKET MESSAGES (server → client)");
  lines.push("  joined, snapshot, match_state, damage, kill_feed");
  lines.push("  pong, match_stats");
  lines.push("");
  lines.push("PERFORMANCE DESIGN (100 players)");
  lines.push("  • Snapshots rate-limited to SNAPSHOT_RATE_HZ (default 20), not per-pose");
  lines.push("  • MAX_POSE_MSG_PER_SEC default 64 (matches Unity MatchPresenceSync send rate)");
  lines.push("  • match_state decoupled: phase/score changes + 1 Hz timer tick");
  lines.push("  • JSON.stringify in worker; master sends pre-serialized WS frames");
  lines.push("  • ping/pong handled on master WS (no worker IPC)");
  lines.push("  • ensurePlayerForQueue: fast enqueue; bot-player-* skips DB");
  lines.push("  • makeMatch uses duelRating from ticket (no getProfile round-trip)");
  lines.push("  • WS permessage-deflate for JSON compression");
  lines.push("  • Binary poses from Unity (RTP1) — less parse cost than JSON");
  lines.push("  • Binary snapshots RTS1 v12 — smaller than JSON snapshot frames");
  lines.push("  • server.js verifySnapshotBinaryCodec() on startup (fail fast on codec drift)");
  lines.push("");
  lines.push("CAPACITY MATH (defaults)");
  lines.push("  100 players = 50 matches × 2");
  lines.push("  WORKER_COUNT=4, MAX_MATCHES_PER_WORKER=50 → max 200 matches theoretical");
  lines.push("  MAX_CONCURRENT_DUEL_MATCHES=150 → hard cap 300 players in combat");
  lines.push("  Inbound:  ~6400 pose/s max (64 Hz/client × 100, rate-limited)");
  lines.push("  Outbound: ~2000 snapshot/s (20 Hz × 100) + ~100 match_state/s");
  lines.push("");
  lines.push("RECOMMENDED .env FOR VPS 6 vCPU / 12 GB RAM");
  lines.push("-".repeat(72));
  lines.push("  PORT=5050");
  lines.push("  REALTIME_WS_PORT=5051");
  lines.push("  MATCH_SERVER_ADDRESS=<your-domain-or-ip>");
  lines.push("  MATCH_SERVER_PORT=5050");
  lines.push("  DATABASE_URL=postgres://...");
  lines.push("  WORKER_COUNT=4");
  lines.push("  MAX_MATCHES_PER_WORKER=50");
  lines.push("  MAX_CONCURRENT_DUEL_MATCHES=150");
  lines.push("  SERVER_TICK_RATE=30");
  lines.push("  SNAPSHOT_RATE_HZ=20");
  lines.push("  USE_BINARY_SNAPSHOTS=1");
  lines.push("  MAX_POSE_MSG_PER_SEC=64");
  lines.push("  WS_MAX_BUFFERED_BYTES=524288");
  lines.push("  WS_MAX_BUFFERED_HARD_BYTES=1048576");
  lines.push("  MATCH_TIMEOUT_SECONDS=15");
  lines.push("  DUEL_PREP_MS=15000");
  lines.push("  DUEL_WEAPON_PICK_MS=10000");
  lines.push("  DUEL_ROUND_MS=60000");
  lines.push("  DUEL_ROUND_END_MS=3000");
  lines.push("  DUEL_ROUNDS_TO_WIN=5");
  lines.push("  BOTS_ENABLED=0          # enable only for load tests");
  lines.push("  BOT_AUTO_FILL_TARGET=0");
  lines.push("");
  lines.push("DEPLOY");
  lines.push("  systemctl restart shooter-queue");
  lines.push("  journalctl -u shooter-queue -f");
  lines.push("  curl http://127.0.0.1:5050/health");
  lines.push("");
  lines.push("LOAD TEST BOTS");
  lines.push("  BOTS_ENABLED=1 in .env + restart");
  lines.push("  curl -X POST http://127.0.0.1:5050/admin/bots/spawn -d '{\"count\":100}'");
  lines.push("  curl -X POST http://127.0.0.1:5050/admin/bots/stop");
  lines.push("  Note: WS bots share master CPU with humans — use separate VPS for pure load test");
  lines.push("");
  return lines.join("\n");
}

function buildFunctionIndex() {
  const sections = [
    ["server.js", readText("server.js") || ""],
    ["workers/manager.js", readText("workers/manager.js") || ""],
    ["workers/gameWorker.js", readText("workers/gameWorker.js") || ""],
    ["workers/duel.js", readText("workers/duel.js") || ""],
    ["workers/clientProtocol.js", readText("workers/clientProtocol.js") || ""],
    ["network/websocket.js", readText("network/websocket.js") || ""],
    ["network/snapshotBinary.js", readText("network/snapshotBinary.js") || ""],
    ["lib/wsOutbound.js", readText("lib/wsOutbound.js") || ""],
    ["bots/botManager.js", readText("bots/botManager.js") || ""],
  ];

  const lines = [];
  lines.push("KEY SOURCE INDEX (functions / classes)");
  lines.push("-".repeat(72));
  for (const [file, source] of sections) {
    if (!source) {
      continue;
    }
    lines.push("");
    lines.push(file);
    for (const row of parseTopLevelFunctions(source, "")) {
      lines.push(`  L${String(row.line).padStart(5)}  ${row.name}`);
    }
  }
  return lines.join("\n");
}

const bundle = { tree: [], files: [] };

for (const dir of INCLUDE_DIRS) {
  walkDir(path.join(root, dir), dir, bundle);
}

for (const rel of ROOT_FILES) {
  if (!shouldExclude(rel) && fs.existsSync(path.join(root, rel))) {
    bundle.tree.push(rel);
    if (!bundle.files.includes(rel)) {
      bundle.files.push(rel);
    }
  }
}

bundle.tree = [...new Set(bundle.tree)].sort();
bundle.files = [...new Set(bundle.files)].sort();

const parts = [];
parts.push("SHOOTERPROTOTYPE — DUEL SERVER ARCHITECTURE & FULL SOURCE CODE");
parts.push(`Generated: ${new Date().toISOString()}`);
parts.push("");
parts.push("SCOPE: Production duel 1v1 server (worker-thread architecture).");
parts.push("       Legacy monolith: server.legacy.js + duelMatch.js (excluded from bundle).");
parts.push("");
parts.push(buildArchitectureOverview());
parts.push("");
parts.push(buildFunctionIndex());
parts.push("");
parts.push("FULL DIRECTORY TREE");
parts.push("-".repeat(72));
parts.push(bundle.tree.join("\n"));

for (const rel of bundle.files) {
  parts.push("");
  parts.push("=".repeat(88));
  parts.push(`FILE: ${rel}`);
  parts.push("=".repeat(88));
  parts.push(readText(rel) || "(missing)");
}

fs.writeFileSync(outFile, parts.join("\n"), "utf8");
const mb = (fs.statSync(outFile).size / 1024 / 1024).toFixed(2);
console.log(`Wrote ${outFile} (${mb} MB, ${bundle.files.length} files)`);
