"use strict";

function toInt(value, fallback) {
  const n = Number.parseInt(String(value), 10);
  return Number.isFinite(n) ? n : fallback;
}

function toFloat(value, fallback) {
  const n = Number(value);
  return Number.isFinite(n) ? n : fallback;
}

module.exports = {
  port: toInt(process.env.PORT, 5050),
  realtimeWsPort: toInt(process.env.REALTIME_WS_PORT, 5051),
  matchServerAddress: process.env.MATCH_SERVER_ADDRESS || "127.0.0.1",
  matchServerPort: toInt(process.env.MATCH_SERVER_PORT, 5050),
  workerCount: Math.max(1, toInt(process.env.WORKER_COUNT, 3)),
  maxMatchesPerWorker: Math.max(10, toInt(process.env.MAX_MATCHES_PER_WORKER, 50)),
  maxConcurrentDuelMatches: Math.max(1, toInt(process.env.MAX_CONCURRENT_DUEL_MATCHES, 150)),
  matchTimeoutSeconds: Math.max(3, toInt(process.env.MATCH_TIMEOUT_SECONDS, 20)),
  tickRateHz: Math.max(20, toInt(process.env.SERVER_TICK_RATE, 60)),
  snapshotRateHz: Math.max(10, Math.min(30, toInt(process.env.SNAPSHOT_RATE_HZ, 20))),
  useBinarySnapshots: (process.env.USE_BINARY_SNAPSHOTS || "0") === "1",
  roundsToWin: Math.max(1, toInt(process.env.DUEL_ROUNDS_TO_WIN, 5)),
  weaponPickTimeoutMs: Math.max(3000, toInt(process.env.DUEL_WEAPON_PICK_MS, 10000)),
  roundTimeoutMs: Math.max(10000, toInt(process.env.DUEL_ROUND_MS, 60000)),
  roundEndTimeoutMs: Math.max(1000, toInt(process.env.DUEL_ROUND_END_MS, 3000)),
  prepTimeoutMs: Math.max(3000, toInt(process.env.DUEL_PREP_MS, 15000)),
  botsEnabled: (process.env.BOTS_ENABLED || "0") === "1",
  botAutoFillTarget: Math.max(0, toInt(process.env.BOT_AUTO_FILL_TARGET, 0)),
  aiBotsEnabled: (process.env.AI_BOTS_ENABLED || "1") === "1",
  aiBotAutoFillSolo: (process.env.AI_BOT_AUTO_FILL_SOLO || "1") === "1",
  aiBotSoloWaitMs: Math.max(500, toInt(process.env.AI_BOT_SOLO_WAIT_MS, 2500)),
  aiThinkHz: Math.max(5, Math.min(20, toInt(process.env.AI_BOT_THINK_HZ, 10))),
  botHttpHost: process.env.BOT_HTTP_HOST || "127.0.0.1",
  botHttpPort: toInt(process.env.BOT_HTTP_PORT, 0) || toInt(process.env.PORT, 5050),
  botWsHost: process.env.BOT_WS_HOST || "127.0.0.1",
  botWsPort: toInt(process.env.BOT_WS_PORT, 0) || toInt(process.env.REALTIME_WS_PORT, 5051),
  maxPlayerSpeed: Math.max(4, toFloat(process.env.MAX_PLAYER_SPEED, 12.5)),
  maxTeleportDistance: Math.max(2, toFloat(process.env.MAX_TELEPORT_DISTANCE, 8)),
  playerHitRadius: Math.max(0.4, toFloat(process.env.PLAYER_HIT_RADIUS, 0.95)),
  debugRealtime: (process.env.DEBUG_REALTIME || "0") === "1",
};
