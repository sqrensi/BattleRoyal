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
  workerCount: Math.max(1, toInt(process.env.WORKER_COUNT, 4)),
  maxMatchesPerWorker: Math.max(10, toInt(process.env.MAX_MATCHES_PER_WORKER, 50)),
  maxConcurrentDuelMatches: Math.max(1, toInt(process.env.MAX_CONCURRENT_DUEL_MATCHES, 150)),
  maxConcurrentDeathmatchMatches: Math.max(1, toInt(process.env.MAX_CONCURRENT_DM_MATCHES, 80)),
  matchTimeoutSeconds: Math.max(2, toInt(process.env.MATCH_TIMEOUT_SECONDS, 15)),
  dmMinPlayers: Math.max(2, Math.min(6, toInt(process.env.DM_MIN_PLAYERS, 2))),
  dmMaxPlayers: Math.max(2, Math.min(6, toInt(process.env.DM_MAX_PLAYERS, 6))),
  dmQueueTimeoutMs: Math.max(2000, toInt(process.env.DM_QUEUE_TIMEOUT_MS, 8000)),
  dmMatchDurationMs: Math.max(60000, toInt(process.env.DM_MATCH_DURATION_MS, 600000)),
  dmRespawnDelayMs: Math.max(1000, toInt(process.env.DM_RESPAWN_DELAY_MS, 3000)),
  tickRateHz: Math.max(20, toInt(process.env.SERVER_TICK_RATE, 30)),
  snapshotRateHz: Math.max(10, Math.min(60, toInt(process.env.SNAPSHOT_RATE_HZ, 40))),
  snapshotHistorySamples: Math.max(4, Math.min(16, toInt(process.env.SNAPSHOT_HISTORY_SAMPLES, 12))),
  useBinarySnapshots: (process.env.USE_BINARY_SNAPSHOTS || "1") === "1",
  wsMaxBufferedBytes: Math.max(65536, toInt(process.env.WS_MAX_BUFFERED_BYTES, 524288)),
  wsMaxBufferedHardBytes: Math.max(131072, toInt(process.env.WS_MAX_BUFFERED_HARD_BYTES, 1048576)),
  maxPoseMessagesPerSecond: Math.max(5, toInt(process.env.MAX_POSE_MSG_PER_SEC, 64)),
  maxPingMessagesPerSecond: Math.max(1, toInt(process.env.MAX_PING_MSG_PER_SEC, 5)),
  maxShotMessagesPerSecond: Math.max(1, toInt(process.env.MAX_SHOT_MSG_PER_SEC, 15)),
  maxHitMessagesPerSecond: Math.max(1, toInt(process.env.MAX_HIT_MSG_PER_SEC, 20)),
  botSpawnMax: Math.max(0, Math.min(500, toInt(process.env.BOT_SPAWN_MAX, 500))),
  botSpawnChunkSize: Math.max(5, toInt(process.env.BOT_SPAWN_CHUNK, 25)),
  queueDbConcurrency: Math.max(4, toInt(process.env.QUEUE_DB_CONCURRENCY, 15)),
  maintenanceDbConcurrency: Math.max(1, toInt(process.env.MAINTENANCE_DB_CONCURRENCY, 5)),
  queueProfileCacheTtlMs: Math.max(5000, toInt(process.env.QUEUE_PROFILE_CACHE_TTL_MS, 30000)),
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
