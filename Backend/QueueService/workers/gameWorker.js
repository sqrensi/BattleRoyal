"use strict";

const { parentPort, workerData } = require("worker_threads");
const Duel = require("./duel");
const { MasterToWorker, WorkerToMaster, PlayerEventType } = require("./protocol");
const { maybeEncodeBinary } = require("./snapshot");

const limits = {
  tickRateHz: workerData.tickRateHz,
  snapshotRateHz: workerData.snapshotRateHz,
  useBinarySnapshots: workerData.useBinarySnapshots,
  roundsToWin: workerData.roundsToWin,
  weaponPickTimeoutMs: workerData.weaponPickTimeoutMs,
  roundTimeoutMs: workerData.roundTimeoutMs,
  roundEndTimeoutMs: workerData.roundEndTimeoutMs,
  prepTimeoutMs: workerData.prepTimeoutMs,
  matchJoinTimeoutMs: workerData.matchJoinTimeoutMs,
  aiThinkHz: workerData.aiThinkHz,
  maxPlayerSpeed: workerData.maxPlayerSpeed,
  maxTeleportDistance: workerData.maxTeleportDistance,
  playerHitRadius: workerData.playerHitRadius,
};

const matches = new Map();
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
    post(WorkerToMaster.PLAYER_MESSAGE, {
      matchId: match.id,
      ticketId: item.ticketId,
      message: item.message,
    });
  }
}

function broadcastSnapshot(match, nowMs) {
  if (!match.shouldSendSnapshot(nowMs, snapshotIntervalMs)) {
    return;
  }

  match.lastSnapshotMs = nowMs;
  match.dirty = false;

  for (const player of match.players) {
    const frame = match.buildSnapshotForViewer(player.ticketId);
    const encoded = maybeEncodeBinary(frame, limits.useBinarySnapshots);

    post(WorkerToMaster.SNAPSHOT, {
      matchId: match.id,
      ticketId: player.ticketId,
      encoding: encoded.encoding,
      payload: encoded.payload,
    });
  }

  broadcastMatchState(match);
}

function broadcastMatchState(match) {
  for (const player of match.players) {
    post(WorkerToMaster.MATCH_STATE, {
      matchId: match.id,
      ticketId: player.ticketId,
      state: match.buildStateForTicket(player.ticketId),
    });
  }
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

parentPort.on("message", (msg) => {
  if (!msg || !msg.type) {
    return;
  }

  if (msg.type === MasterToWorker.CREATE_MATCH) {
    const duel = new Duel(msg.match, limits);
    matches.set(msg.match.id, duel);
    post(WorkerToMaster.MATCH_CREATED, { matchId: msg.match.id, playerTicketIds: duel.players.map((p) => p.ticketId) });
    broadcastMatchState(duel);
    return;
  }

  if (msg.type === MasterToWorker.SHUTDOWN) {
    running = false;
    matches.clear();
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
    const match = matches.get(msg.matchId);
    if (!match || match.finished) {
      return;
    }

    const nowMs = Date.now();
    const { ticketId, eventType, payload } = msg;

    switch (eventType) {
      case PlayerEventType.JOIN:
        match.onJoin(ticketId);
        post(WorkerToMaster.PLAYER_MESSAGE, {
          matchId: match.id,
          ticketId,
          message: { type: "joined", ticketId, matchId: match.id },
        });
        broadcastMatchState(match);
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
      case PlayerEventType.PING:
        post(WorkerToMaster.PLAYER_MESSAGE, {
          matchId: match.id,
          ticketId,
          message: { type: "pong", clientTimeMs: payload && payload.clientTimeMs },
        });
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
      return;
    }

    broadcastSnapshot(match, nowMs);
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
