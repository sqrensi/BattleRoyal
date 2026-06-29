"use strict";

const { parentPort, workerData } = require("worker_threads");
const config = require("../config");
const log = require("../lib/logger");
const Duel = require("./duel");
const Deathmatch = require("./deathmatch");
const { MasterToWorker, WorkerToMaster, PlayerEventType } = require("./protocol");
const { maybeEncodeBinary } = require("./snapshot");

const limits = {
  tickRateHz: workerData.tickRateHz,
  snapshotRateHz: workerData.snapshotRateHz,
  poseSampleRateHz: workerData.poseSampleRateHz,
  snapshotHistorySamples: workerData.snapshotHistorySamples,
  useBinarySnapshots: workerData.useBinarySnapshots,
  roundsToWin: workerData.roundsToWin,
  weaponPickTimeoutMs: workerData.weaponPickTimeoutMs,
  roundTimeoutMs: workerData.roundTimeoutMs,
  roundEndTimeoutMs: workerData.roundEndTimeoutMs,
  prepTimeoutMs: workerData.prepTimeoutMs,
  matchJoinTimeoutMs: workerData.matchJoinTimeoutMs,
  dmMinPlayers: workerData.dmMinPlayers,
  dmMaxPlayers: workerData.dmMaxPlayers,
  dmMatchDurationMs: workerData.dmMatchDurationMs,
  dmRespawnDelayMs: workerData.dmRespawnDelayMs,
  aiThinkHz: workerData.aiThinkHz,
  maxPlayerSpeed: workerData.maxPlayerSpeed,
  maxTeleportDistance: workerData.maxTeleportDistance,
  playerHitRadius: workerData.playerHitRadius,
};

const matches = new Map();
const pendingPlayerEvents = new Map();
const tickIntervalMs = Math.max(1, Math.floor(1000 / limits.tickRateHz));
const snapshotIntervalMs = Math.max(1, Math.floor(1000 / limits.snapshotRateHz));

let running = true;
let lastTickMs = Date.now();
let tickCount = 0;

function post(type, payload) {
  parentPort.postMessage({ type, ...payload });
}

function sendToPlayers(match, fn) {
  for (const player of match.players) {
    fn(player.ticketId);
  }
}

function deliverOutbox(match) {
  for (const item of match.flushOutbox()) {
    const message = item.message;
    post(WorkerToMaster.PLAYER_MESSAGE, {
      matchId: match.id,
      ticketId: item.ticketId,
      preSerialized: typeof message === "object" && message !== null,
      message: typeof message === "object" && message !== null ? JSON.stringify(message) : message,
    });
  }
}

function broadcastSnapshot(match, nowMs, force) {
  if (!force && !match.shouldSendSnapshot(nowMs, snapshotIntervalMs)) {
    return;
  }

  match.lastSnapshotMs = nowMs;
  match.snapshotDirty = false;

  for (const player of match.players) {
    const frame = match.buildSnapshotForViewer(player.ticketId);
    const encoded = maybeEncodeBinary(frame, limits.useBinarySnapshots);
    if (encoded.encoding === "json") {
      post(WorkerToMaster.SNAPSHOT, {
        matchId: match.id,
        ticketId: player.ticketId,
        encoding: "json",
        preSerialized: true,
        payload: JSON.stringify(encoded.payload),
      });
      continue;
    }

    const binaryPayload = encoded.payload;
    const binaryValid = Buffer.isBuffer(binaryPayload) &&
      binaryPayload.length >= 4 &&
      binaryPayload.toString("ascii", 0, 4) === "RTS1";
    if (!binaryValid) {
      log.warn("snapshot", `invalid RTS1 payload for ${String(player.ticketId).slice(0, 8)}, sending JSON`);
      post(WorkerToMaster.SNAPSHOT, {
        matchId: match.id,
        ticketId: player.ticketId,
        encoding: "json",
        preSerialized: true,
        payload: JSON.stringify(frame),
      });
      continue;
    }

    post(WorkerToMaster.SNAPSHOT, {
      matchId: match.id,
      ticketId: player.ticketId,
      encoding: encoded.encoding,
      payload: binaryPayload,
    });
  }
}

function broadcastMatchState(match) {
  for (const player of match.players) {
    const state = match.buildStateForTicket(player.ticketId);
    post(WorkerToMaster.MATCH_STATE, {
      matchId: match.id,
      ticketId: player.ticketId,
      preSerialized: true,
      state: JSON.stringify(state),
    });
  }
  match.stateDirty = false;
  match.lastStateBroadcastMs = Date.now();
}

function maybeBroadcastMatchState(match, nowMs) {
  const timerActive = match.timerEndsAtMs > nowMs;
  const timerTickDue = timerActive && nowMs - match.lastStateBroadcastMs >= 1000;
  if (!match.stateDirty && !timerTickDue) {
    return;
  }

  broadcastMatchState(match);
}

function removeMatch(matchId) {
  matches.delete(matchId);
}

function reportWorkerError(scope, error) {
  const detail = error && (error.stack || error.message) ? (error.stack || error.message) : String(error);
  post(WorkerToMaster.ERROR, { message: `[${scope}] ${detail}`, fatal: true });
}

parentPort.on("error", (error) => {
  reportWorkerError("parentPort", error);
});

process.on("uncaughtException", (error) => {
  reportWorkerError("uncaughtException", error);
});

process.on("unhandledRejection", (reason) => {
  reportWorkerError("unhandledRejection", reason instanceof Error ? reason : new Error(String(reason)));
});

function flushPendingPlayerEvents(matchId) {
  const pending = pendingPlayerEvents.get(matchId);
  if (!pending || pending.length === 0) {
    return;
  }
  pendingPlayerEvents.delete(matchId);
  for (const msg of pending) {
    handlePlayerEvent(msg);
  }
}

function handlePlayerEvent(msg) {
  const match = matches.get(msg.matchId);
  if (!match || match.finished) {
    return false;
  }

  const nowMs = Date.now();
  const { ticketId, eventType, payload } = msg;

  switch (eventType) {
    case PlayerEventType.JOIN:
      match.onJoin(ticketId);
      post(WorkerToMaster.PLAYER_MESSAGE, {
        matchId: match.id,
        ticketId,
        preSerialized: true,
        message: JSON.stringify({ type: "joined", ticketId, matchId: match.id }),
      });
      broadcastMatchState(match);
      broadcastSnapshot(match, nowMs, true);
      {
        const joined = match.getPlayer(ticketId);
        const connectedCount = match.players.filter((p) => p.connected).length;
        const frame = match.buildSnapshotForViewer(ticketId);
        const remoteCount = Array.isArray(frame.players) ? frame.players.length : 0;
        log.info(
          "ws",
          `join worker ticket=${String(ticketId).slice(0, 8)} connected=${joined && joined.connected ? 1 : 0} ` +
          `matchConnected=${connectedCount}/${match.players.length} snapshotRemotes=${remoteCount} phase=${match.phase}`
        );
      }
      break;
    case PlayerEventType.POSE:
      match.onPose(ticketId, payload || {}, nowMs);
      break;
    case PlayerEventType.WEAPON_PICK:
      match.onWeaponPick(ticketId, payload || {}, nowMs);
      broadcastMatchState(match);
      break;
    case PlayerEventType.SHOT:
      match.onShot(ticketId, payload || {}, nowMs);
      break;
    case PlayerEventType.HIT:
      match.onHit(ticketId, payload || {}, nowMs);
      deliverOutbox(match);
      broadcastMatchState(match);
      break;
    default:
      break;
  }

  if (match.finished) {
    broadcastMatchState(match);
    post(WorkerToMaster.MATCH_FINISHED, {
      matchId: match.id,
      winnerTicketId: match.winnerTicketId,
      roundWins: match.roundWins,
    });
    removeMatch(match.id);
    return true;
  }

  if (match.stateDirty) {
    maybeBroadcastMatchState(match, nowMs);
  }
  return true;
}

parentPort.on("message", (msg) => {
  if (!msg || !msg.type) {
    return;
  }

  if (msg.type === MasterToWorker.CREATE_MATCH) {
    const matchInstance = msg.match && msg.match.mode === "deathmatch"
      ? new Deathmatch(msg.match, limits)
      : new Duel(msg.match, limits);
    matches.set(msg.match.id, matchInstance);
    post(WorkerToMaster.MATCH_CREATED, {
      matchId: msg.match.id,
      playerTicketIds: matchInstance.players.map((p) => p.ticketId),
    });
    flushPendingPlayerEvents(msg.match.id);
    broadcastMatchState(matchInstance);
    return;
  }

  if (msg.type === MasterToWorker.SHUTDOWN) {
    running = false;
    matches.clear();
    pendingPlayerEvents.clear();
    return;
  }

  if (msg.type === MasterToWorker.PLAYER_DISCONNECT) {
    const match = matches.get(msg.matchId);
    if (!match) {
      return;
    }
    match.onDisconnect(msg.ticketId);
    deliverOutbox(match);
    if (match.finished) {
      broadcastMatchState(match);
      post(WorkerToMaster.MATCH_FINISHED, {
        matchId: match.id,
        winnerTicketId: match.winnerTicketId,
        roundWins: match.roundWins,
      });
      removeMatch(match.id);
    }
    return;
  }

  if (msg.type === MasterToWorker.PLAYER_EVENT) {
    if (!matches.get(msg.matchId)) {
      if (!pendingPlayerEvents.has(msg.matchId)) {
        pendingPlayerEvents.set(msg.matchId, []);
      }
      pendingPlayerEvents.get(msg.matchId).push(msg);
      return;
    }

    handlePlayerEvent(msg);
    return;
  }
});

const loop = setInterval(() => {
  if (!running) {
    clearInterval(loop);
    post(WorkerToMaster.WORKER_READY, { shuttingDown: true });
    return;
  }

  const nowMs = Date.now();
  const delay = nowMs - lastTickMs;
  lastTickMs = nowMs;
  tickCount += 1;

  for (const match of matches.values()) {
    try {
      match.tick(nowMs);
      deliverOutbox(match);
      maybeBroadcastMatchState(match, nowMs);
      broadcastSnapshot(match, nowMs);

      if (match.finished) {
        broadcastMatchState(match);
        post(WorkerToMaster.MATCH_FINISHED, {
          matchId: match.id,
          winnerTicketId: match.winnerTicketId,
          roundWins: match.roundWins,
        });
        removeMatch(match.id);
      }
    } catch (error) {
      const detail = error && (error.stack || error.message) ? (error.stack || error.message) : String(error);
      post(WorkerToMaster.ERROR, { message: `[match ${match.id}] ${detail}`, fatal: false });
      post(WorkerToMaster.MATCH_FINISHED, {
        matchId: match.id,
        winnerTicketId: "",
        roundWins: match.roundWins || {},
        abandoned: true,
      });
      removeMatch(match.id);
    }
  }

  if (tickCount % limits.tickRateHz === 0) {
    post(WorkerToMaster.METRICS, {
      matchCount: matches.size,
      tickDelayMs: delay,
      tickRateHz: limits.tickRateHz,
    });
  }
}, tickIntervalMs);

post(WorkerToMaster.WORKER_READY, { workerId: workerData.workerId });
