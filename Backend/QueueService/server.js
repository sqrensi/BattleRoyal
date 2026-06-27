"use strict";

const http = require("http");
const crypto = require("crypto");
const { URL } = require("url");

const config = require("./config");
const { enableCors, respondJson, readJsonBody, normalizePlayerId } = require("./lib/http");
const log = require("./lib/logger");
const Metrics = require("./lib/metrics");
const WorkerManager = require("./workers/manager");
const { WorkerToMaster } = require("./workers/protocol");
const { createRealtimeServer } = require("./network/websocket");
const { initDatabase } = require("./db/database");
const { registerProfileRoutes } = require("./db/profileRoutes");
const playerRepository = require("./db/playerRepository");

function verifySnapshotBinaryCodec() {
  try {
    const { encodeSnapshotRts1, VERSION } = require("./network/snapshotBinary");
    const sample = encodeSnapshotRts1({
      serverTick: 1,
      serverTickRate: 30,
      movementSampleRateHz: 64,
      players: [{
        ticketId: "verify-ticket",
        characterModel: "Ch18",
        position: { x: 39.05, y: 0.606, z: 288.21 },
        yaw: 0,
        hasWeapon: false,
        weaponKind: 0,
        weaponSlot0Kind: 255,
        weaponSlot1Kind: 255,
        activeWeaponSlot: 255,
      }],
    });
    if (!Buffer.isBuffer(sample) || sample.length < 16 || sample.toString("ascii", 0, 4) !== "RTS1") {
      throw new Error("encodeSnapshotRts1 produced invalid buffer");
    }
    if (sample[4] !== VERSION) {
      throw new Error(`snapshot version byte ${sample[4]} != ${VERSION}`);
    }
    log.info("snapshot", `binary codec OK version=${VERSION} sampleBytes=${sample.length}`);
  } catch (error) {
    log.error("snapshot", `binary codec verify failed: ${error.message || error}`);
    process.exit(1);
  }
}

verifySnapshotBinaryCodec();

function asSnapshotBuffer(payload) {
  if (Buffer.isBuffer(payload)) {
    return payload;
  }
  if (payload instanceof Uint8Array) {
    return Buffer.from(payload);
  }
  if (payload && payload.type === "Buffer" && Array.isArray(payload.data)) {
    return Buffer.from(payload.data);
  }
  return null;
}

const pendingSnapshotsByTicket = new Map();

function queueSnapshotForTicket(ticketId, msg) {
  if (!ticketId || !msg) {
    return;
  }
  if (!pendingSnapshotsByTicket.has(ticketId)) {
    pendingSnapshotsByTicket.set(ticketId, []);
  }
  const queue = pendingSnapshotsByTicket.get(ticketId);
  queue.push(msg);
  while (queue.length > 48) {
    queue.shift();
  }
}

function flushPendingSnapshotForTicket(ticketId) {
  const queue = pendingSnapshotsByTicket.get(ticketId);
  if (!queue || queue.length === 0) {
    return;
  }

  while (queue.length > 0) {
    const pending = queue[0];
    if (!sendSnapshotToTicket(ticketId, pending, { fromQueue: true })) {
      break;
    }
    queue.shift();
  }

  if (queue.length === 0) {
    pendingSnapshotsByTicket.delete(ticketId);
  }
}

function sendSnapshotToTicket(ticketId, msg, options) {
  const fromQueue = !!(options && options.fromQueue);
  if (msg.encoding === "binary") {
    const buffer = asSnapshotBuffer(msg.payload);
    if (buffer && buffer.length >= 4 && buffer.toString("ascii", 0, 4) === "RTS1") {
      if (wsApi.sendBinary(ticketId, buffer)) {
        if (!fromQueue) {
          pendingSnapshotsByTicket.delete(ticketId);
        }
        return true;
      }
      if (!fromQueue) {
        queueSnapshotForTicket(ticketId, { encoding: "binary", payload: buffer });
      }
      return false;
    }
    log.warn(
      "snapshot",
      `invalid binary snapshot payload ticket=${String(ticketId).slice(0, 8)} ` +
      `isBuffer=${Buffer.isBuffer(msg.payload)} len=${buffer ? buffer.length : 0}`
    );
    return false;
  }

  if (msg.preSerialized && typeof msg.payload === "string") {
    if (wsApi.sendRaw(ticketId, msg.payload, undefined, "snapshot")) {
      if (!fromQueue) {
        pendingSnapshotsByTicket.delete(ticketId);
      }
      return true;
    }
    if (!fromQueue) {
      queueSnapshotForTicket(ticketId, msg);
    }
    return false;
  }

  if (typeof msg.payload === "string") {
    if (wsApi.sendRaw(ticketId, msg.payload, undefined, "snapshot")) {
      if (!fromQueue) {
        pendingSnapshotsByTicket.delete(ticketId);
      }
      return true;
    }
    if (!fromQueue) {
      queueSnapshotForTicket(ticketId, msg);
    }
    return false;
  }

  if (wsApi.sendJson(ticketId, msg.payload)) {
    if (!fromQueue) {
      pendingSnapshotsByTicket.delete(ticketId);
    }
    return true;
  }
  if (!fromQueue) {
    queueSnapshotForTicket(ticketId, msg);
  }
  return false;
}

const metrics = new Metrics();
let databaseDriver = "sqlite";
let shuttingDown = false;
let botManager = null;

if (config.botsEnabled) {
  const BotManager = require("./bots/botManager");
  botManager = new BotManager();
}

const ticketsById = new Map();
const queue = [];
const matchesById = new Map();
const ticketToMatch = new Map();

const workers = new WorkerManager(config.workerCount, {});
workers.onWorkerReady(() => {
  void tryMatchQueue();
});
const tickIntervalMs = Math.max(1, Math.floor(1000 / config.tickRateHz));

const handleProfileRoutes = registerProfileRoutes({
  readJsonBody,
  respondJson,
  getRequestUrl: (req) => new URL(req.url, `http://${req.headers.host || "127.0.0.1"}`),
});

function normalizeMatchMode(value) {
  const raw = String(value || "duel").trim().toLowerCase();
  if (raw === "duel" || raw === "1v1" || raw === "duel_1v1") {
    return "duel";
  }
  return "duel";
}

function createTicket(playerId, matchMode, nickname, duelRating) {
  const ticketId = crypto.randomUUID();
  const ticket = {
    ticketId,
    playerId,
    nickname: nickname || "",
    duelRating: Number.isFinite(duelRating) ? Math.max(0, duelRating) : 1000,
    matchMode: normalizeMatchMode(matchMode),
    status: "Queued",
    matchId: "",
    createdAtMs: Date.now(),
    serverAddress: config.matchServerAddress,
    serverPort: config.matchServerPort,
    socket: null,
  };
  ticketsById.set(ticketId, ticket);
  return ticket;
}

function cancelQueuedTicketsForPlayer(playerId) {
  for (let i = queue.length - 1; i >= 0; i--) {
    const ticket = queue[i];
    if (ticket && ticket.playerId === playerId && ticket.status === "Queued") {
      ticket.status = "Cancelled";
      queue.splice(i, 1);
    }
  }
}

function findActiveMatchedTicket(playerId) {
  for (const ticket of ticketsById.values()) {
    if (!ticket || ticket.playerId !== playerId || ticket.status !== "Matched" || !ticket.matchId) {
      continue;
    }
    const match = matchesById.get(ticket.matchId);
    if (match && match.state !== "ended") {
      return ticket;
    }
  }
  return null;
}

function countActiveMatches() {
  let count = 0;
  for (const match of matchesById.values()) {
    if (match.state !== "ended") {
      count += 1;
    }
  }
  return count;
}

async function makeMatch(playerA, playerB) {
  if (countActiveMatches() >= config.maxConcurrentDuelMatches) {
    return null;
  }

  const nickA = playerA.nickname ||
    await playerRepository.resolvePlayerNickname(playerA.playerId, playerA.ticketId);
  const nickB = playerB.nickname ||
    await playerRepository.resolvePlayerNickname(playerB.playerId, playerB.ticketId);
  const duelRatingA = Number.isFinite(playerA.duelRating)
    ? Math.max(0, playerA.duelRating)
    : 1000;
  const duelRatingB = Number.isFinite(playerB.duelRating)
    ? Math.max(0, playerB.duelRating)
    : 1000;
  playerA.nickname = nickA;
  playerB.nickname = nickB;

  const matchId = crypto.randomUUID();
  const match = {
    id: matchId,
    mode: "duel",
    state: "creating",
    players: [playerA, playerB],
    ticketIds: [playerA.ticketId, playerB.ticketId],
    createdAtMs: Date.now(),
    workerId: null,
  };

  const assigned = workers.assign({
    id: matchId,
    mode: "duel",
    players: [
      {
        ticketId: playerA.ticketId,
        playerId: playerA.playerId,
        nickname: nickA,
        duelRating: duelRatingA,
      },
      {
        ticketId: playerB.ticketId,
        playerId: playerB.playerId,
        nickname: nickB,
        duelRating: duelRatingB,
      },
    ],
  });

  if (!assigned) {
    return null;
  }

  matchesById.set(matchId, match);
  for (const ticket of [playerA, playerB]) {
    ticket.status = "Matched";
    ticket.matchId = matchId;
    ticket.matchedAtMs = Date.now();
    ticketToMatch.set(ticket.ticketId, matchId);
  }

  metrics.setActiveMatches(countActiveMatches());
  return match;
}

function buildTicketResponse(ticket) {
  const match = ticket.matchId ? matchesById.get(ticket.matchId) : null;
  const matchedPlayerCount = match && match.ticketIds ? match.ticketIds.length : (ticket.status === "Matched" ? 2 : 0);
  return {
    ticketId: ticket.ticketId,
    playerId: ticket.playerId,
    status: ticket.status,
    queueDurationSeconds: Math.max(0, (Date.now() - ticket.createdAtMs) / 1000),
    matchId: ticket.matchId,
    matchedPlayerCount,
    serverAddress: ticket.serverAddress,
    serverPort: ticket.serverPort,
    matchMode: ticket.matchMode,
  };
}

async function tryMatchQueue() {
  while (queue.length >= 2 && countActiveMatches() < config.maxConcurrentDuelMatches) {
    let indexA = -1;
    let indexB = -1;

    outer:
    for (let i = 0; i < queue.length - 1; i++) {
      for (let j = i + 1; j < queue.length; j++) {
        if (queuePairCompatible(queue[i], queue[j])) {
          indexA = i;
          indexB = j;
          break outer;
        }
      }
    }

    if (indexA < 0 || indexB < 0) {
      break;
    }

    const b = queue.splice(indexB, 1)[0];
    const a = queue.splice(indexA, 1)[0];
    if (!a || !b) {
      break;
    }

    const match = await makeMatch(a, b);
    if (!match) {
      queue.unshift(b);
      queue.unshift(a);
      log.warn(
        "match",
        `queue stalled active=${countActiveMatches()} max=${config.maxConcurrentDuelMatches} queue=${queue.length}`
      );
      break;
    }
    const wsBotCount = [a, b].filter((ticket) => isWsBotTicket(ticket)).length;
    log.info(
      "match",
      `created ${match.id} players=${a.playerId},${b.playerId} tickets=${a.ticketId.slice(0, 8)},${b.ticketId.slice(0, 8)} wsBots=${wsBotCount} active=${countActiveMatches()} queue=${queue.length}`
    );
  }
  metrics.setQueueSize(queue.length);
}

async function finalizeDuelMatchStats(match, winnerTicketId, roundWins) {
  if (!match || match.mode !== "duel") {
    return;
  }

  const sourceId = `duel:${match.id}`;
  const resolvedWinnerTicketId = resolveDuelWinnerTicketId(
    winnerTicketId,
    roundWins,
    match.ticketIds
  );

  for (const ticketId of match.ticketIds) {
    const ticket = ticketsById.get(ticketId);
    if (!ticket || !ticket.playerId || isWsBotTicket(ticket)) {
      continue;
    }

    const won = !!resolvedWinnerTicketId && ticketId === resolvedWinnerTicketId;
    const kills = Math.max(0, Math.floor(Number(roundWins && roundWins[ticketId]) || 0));

    try {
      const result = await playerRepository.recordMatchStats(ticket.playerId, {
        sourceId,
        kills,
        deaths: won ? 0 : 1,
        placement: won ? 1 : 2,
        won,
        damageDealt: 0,
        matchMode: "duel",
      });
      if (!result.ok) {
        log.warn("match", `stats ${ticket.playerId}: ${result.error || "failed"}`);
        continue;
      }

      deliverMatchStats(ticketId, {
        type: "match_stats",
        sourceId,
        won,
        ratingDelta: result.ratingDelta || 0,
        alreadyReported: !!result.alreadyReported,
        profile: {
          duelRating: result.profile && Number.isFinite(result.profile.duelRating)
            ? result.profile.duelRating
            : 0,
          rating: result.profile && Number.isFinite(result.profile.rating)
            ? result.profile.rating
            : 0,
        },
      });
      log.info(
        "match",
        `stats ${ticket.playerId.slice(0, 8)} won=${won ? 1 : 0} delta=${result.ratingDelta || 0}`
      );
    } catch (error) {
      log.warn("match", `stats ${ticket.playerId}: ${error.message || error}`);
    }
  }
}

function resolveDuelWinnerTicketId(winnerTicketId, roundWins, ticketIds) {
  const normalizedWinner = String(winnerTicketId || "").trim();
  if (normalizedWinner) {
    return normalizedWinner;
  }

  if (!Array.isArray(ticketIds) || ticketIds.length === 0) {
    return "";
  }

  let bestTicketId = "";
  let bestWins = -1;
  for (const ticketId of ticketIds) {
    const wins = Math.max(0, Math.floor(Number(roundWins && roundWins[ticketId]) || 0));
    if (wins > bestWins) {
      bestWins = wins;
      bestTicketId = ticketId;
    }
  }

  if (!bestTicketId || bestWins <= 0) {
    return "";
  }

  let tied = false;
  for (const ticketId of ticketIds) {
    if (ticketId === bestTicketId) {
      continue;
    }
    const wins = Math.max(0, Math.floor(Number(roundWins && roundWins[ticketId]) || 0));
    if (wins === bestWins) {
      tied = true;
      break;
    }
  }

  return tied ? "" : bestTicketId;
}

function deliverMatchStats(ticketId, payload) {
  if (wsApi.sendJson(ticketId, payload)) {
    const ticket = ticketsById.get(ticketId);
    if (ticket) {
      ticket.pendingMatchStats = null;
    }
    return;
  }

  const ticket = ticketsById.get(ticketId);
  if (ticket) {
    ticket.pendingMatchStats = payload;
    log.warn("match", `stats queued for ${ticketId.slice(0, 8)} (socket closed)`);
  }
}

function flushPendingMatchStats(ticketId) {
  const ticket = ticketsById.get(ticketId);
  if (!ticket || !ticket.pendingMatchStats) {
    return;
  }

  if (wsApi.sendJson(ticketId, ticket.pendingMatchStats)) {
    ticket.pendingMatchStats = null;
  }
}

function isWsBotTicket(ticket) {
  return String(ticket && ticket.playerId ? ticket.playerId : "").startsWith("bot-player-");
}

function isBotTicket(ticket) {
  return isWsBotTicket(ticket);
}

function isHumanTicket(ticket) {
  return !!ticket && !isBotTicket(ticket);
}

function queuePairCompatible(a, b) {
  if (!a || !b) {
    return false;
  }

  const wsA = isWsBotTicket(a);
  const wsB = isWsBotTicket(b);
  if (wsA || wsB) {
    return wsA && wsB;
  }

  return isHumanTicket(a) && isHumanTicket(b);
}

function releaseMatchTickets(match, options = {}) {
  const abandoned = !!options.abandoned;
  for (const ticketId of match.ticketIds) {
    const ticket = ticketsById.get(ticketId);
    if (!ticket) {
      continue;
    }

    ticket.matchId = "";
    ticketToMatch.delete(ticketId);

    const neverJoined = !ticket.socket;
    if (abandoned && neverJoined && !isBotTicket(ticket)) {
      ticket.matchId = "";
      ticket.status = "Queued";
      queue.unshift(ticket);
      log.info("match", `requeue ${ticket.ticketId.slice(0, 8)} after abandoned match`);
      continue;
    }

    if (abandoned && neverJoined && isBotTicket(ticket)) {
      ticket.status = "Cancelled";
      continue;
    }

    ticket.status = "Finished";
  }
}

const wsApi = createRealtimeServer({
  bindSocket(ticketId, socket) {
    const ticket = ticketsById.get(ticketId);
    if (!ticket || !ticket.matchId) {
      return false;
    }

    const canBind = ticket.status === "Matched" ||
      (ticket.status === "Queued" && ticket.matchedAtMs && Date.now() - ticket.matchedAtMs < 120000);
    if (!canBind) {
      return false;
    }

    ticket.status = "Matched";
    ticket.socket = socket;
    metrics.adjustPlayersOnline(1);
    flushPendingMatchStats(ticketId);
    log.info("ws", `join ticket=${ticketId.slice(0, 8)} match=${(ticket.matchId || "").slice(0, 8)} player=${ticket.playerId}`);
    flushPendingSnapshotForTicket(ticketId);
    return true;
  },

  routePlayerEvent(ticketId, eventType, payload) {
    const ticket = ticketsById.get(ticketId);
    if (!ticket || !ticket.matchId) {
      return;
    }
    workers.sendPlayerEvent(ticket.matchId, ticketId, eventType, payload);
  },

  onSocketClose(ticketId) {
    const ticket = ticketsById.get(ticketId);
    if (!ticket || !ticket.matchId) {
      return;
    }
    ticket.socket = null;
    workers.sendDisconnect(ticket.matchId, ticketId);
    metrics.adjustPlayersOnline(-1);
  },
});

async function handleMatchFinished(msg) {
  const match = matchesById.get(msg.matchId);
  if (match) {
    match.state = "ended";
    match.winnerTicketId = msg.winnerTicketId || "";
    if (!msg.abandoned) {
      try {
        await finalizeDuelMatchStats(match, msg.winnerTicketId || "", msg.roundWins || {});
      } catch (error) {
        log.warn("match", `stats finalize failed for ${msg.matchId}: ${error.message || error}`);
      }
    }
    releaseMatchTickets(match, { abandoned: !!msg.abandoned });
    matchesById.delete(msg.matchId);
  }

  metrics.setActiveMatches(countActiveMatches());
  void tryMatchQueue();
  if (msg.abandoned) {
    log.warn("match", `abandoned ${msg.matchId} on worker restart`);
  } else {
    log.info("match", `finished ${msg.matchId} winner=${msg.winnerTicketId || "none"}`);
  }
}

workers.onMessage((workerId, msg) => {
  if (msg.type === WorkerToMaster.METRICS) {
    metrics.updateWorker(workerId, {
      matchCount: msg.matchCount,
      tickDelayMs: msg.tickDelayMs,
    });
    if (msg.tickDelayMs > tickIntervalMs * 2) {
      log.warn("tick", `worker ${workerId} slow tick ${msg.tickDelayMs}ms`);
    }
    return;
  }

  if (msg.type === WorkerToMaster.ERROR) {
    log.error("worker", msg.message || "unknown");
    if (msg.fatal !== false) {
      workers.restartWorker(workerId);
    }
    return;
  }

  if (msg.type === WorkerToMaster.MATCH_CREATED) {
    const match = matchesById.get(msg.matchId);
    if (match) {
      match.state = "active";
      match.workerId = workerId;
    }
    return;
  }

  if (msg.type === WorkerToMaster.MATCH_FINISHED) {
    void handleMatchFinished(msg);
    return;
  }

  if (msg.type === WorkerToMaster.SNAPSHOT) {
    const match = matchesById.get(msg.matchId);
    if (!match) {
      return;
    }
    const ticketId = msg.ticketId;
    if (ticketId) {
      sendSnapshotToTicket(ticketId, msg);
      return;
    }
    for (const id of match.ticketIds) {
      sendSnapshotToTicket(id, msg);
    }
    return;
  }

  if (msg.type === WorkerToMaster.MATCH_STATE) {
    const match = matchesById.get(msg.matchId);
    if (!match) {
      return;
    }
    if (msg.ticketId) {
      if (msg.preSerialized && typeof msg.state === "string") {
        wsApi.sendRaw(msg.ticketId, msg.state, undefined, "match_state");
      } else {
        wsApi.sendJson(msg.ticketId, msg.state);
      }
      return;
    }
    for (const ticketId of match.ticketIds) {
      if (msg.preSerialized && typeof msg.state === "string") {
        wsApi.sendRaw(ticketId, msg.state, undefined, "match_state");
      } else {
        wsApi.sendJson(ticketId, msg.state);
      }
    }
    return;
  }

  if (msg.type === WorkerToMaster.PLAYER_MESSAGE) {
    if (msg.preSerialized && typeof msg.message === "string") {
      wsApi.sendRaw(msg.ticketId, msg.message, 2);
    } else if (msg.encoding === "binary") {
      wsApi.sendBinary(msg.ticketId, msg.payload, 2);
    } else {
      wsApi.sendJson(msg.ticketId, msg.message, 2);
    }
  }
});

const server = http.createServer(async (req, res) => {
  enableCors(res);
  if (req.method === "OPTIONS") {
    res.writeHead(204);
    res.end();
    return;
  }

  const requestUrl = new URL(req.url, `http://${req.headers.host || "127.0.0.1"}`);
  const path = requestUrl.pathname;

  if (req.method === "GET" && path === "/health") {
    respondJson(res, 200, {
      ok: true,
      ...metrics.snapshot(),
      workers: workers.stats(),
      database: databaseDriver,
      ws: typeof wsApi.stats === "function" ? wsApi.stats() : null,
      bots: botManager ? botManager.stats() : null,
    });
    return;
  }

  if (botManager && req.method === "POST" && path === "/admin/bots/spawn") {
    const body = await readJsonBody(req);
    const result = await botManager.spawn(body && body.count);
    respondJson(res, 200, { ok: true, ...result });
    return;
  }

  if (botManager && req.method === "POST" && path === "/admin/bots/stop") {
    const result = botManager.stopAll();
    respondJson(res, 200, { ok: true, ...result });
    return;
  }

  if (botManager && req.method === "GET" && path === "/admin/bots/status") {
    respondJson(res, 200, { ok: true, ...botManager.stats() });
    return;
  }

  if (await handleProfileRoutes(req, res, path, req.method)) {
    return;
  }

  if (req.method === "POST" && path === "/enqueue") {
    const body = await readJsonBody(req);
    const playerId = normalizePlayerId(body && body.playerId);
    const matchMode = normalizeMatchMode(body && body.matchMode);
    if (matchMode !== "duel") {
      respondJson(res, 400, { ok: false, error: "OnlyDuelSupported" });
      return;
    }

    const queueProfile = await playerRepository.ensurePlayerForQueue(playerId);

    const activeTicket = findActiveMatchedTicket(playerId);
    if (activeTicket) {
      respondJson(res, 200, buildTicketResponse(activeTicket));
      return;
    }

    cancelQueuedTicketsForPlayer(playerId);

    const ticket = createTicket(
      playerId,
      matchMode,
      queueProfile.nickname,
      queueProfile.duelRating
    );
    queue.push(ticket);
    metrics.setQueueSize(queue.length);
    void tryMatchQueue();

    respondJson(res, 200, buildTicketResponse(ticket));
    return;
  }

  if (req.method === "POST" && path === "/dequeue") {
    const body = await readJsonBody(req);
    const ticketId = body && body.ticketId;
    const ticket = ticketsById.get(ticketId);
    if (!ticket) {
      respondJson(res, 404, { success: false, status: "NotFound" });
      return;
    }
    if (ticket.status === "Queued") {
      ticket.status = "Cancelled";
      const idx = queue.findIndex((t) => t.ticketId === ticketId);
      if (idx >= 0) {
        queue.splice(idx, 1);
      }
    }
    metrics.setQueueSize(queue.length);
    respondJson(res, 200, { success: true, status: ticket.status });
    return;
  }

  if (req.method === "GET" && path.startsWith("/ticket/")) {
    const ticketId = decodeURIComponent(path.replace("/ticket/", ""));
    const ticket = ticketsById.get(ticketId);
    if (!ticket) {
      respondJson(res, 404, { status: "NotFound" });
      return;
    }
    respondJson(res, 200, buildTicketResponse(ticket));
    return;
  }

  respondJson(res, 404, { error: "NotFound" });
});

async function bootstrap() {
  try {
    const info = await initDatabase();
    databaseDriver = info.driver;
    log.info("db", info.message);
  } catch (error) {
    log.error("db", "init failed", error.message || error);
    process.exit(1);
  }

  server.listen(config.port, () => {
    log.info("master", `http://0.0.0.0:${config.port} workers=${config.workerCount}`);
    if (botManager) {
      botManager.startAutoFill();
      log.info("bots", `ws enabled autoFill=${config.botAutoFillTarget}`);
    }
  });
}

function gracefulShutdown(signal) {
  if (shuttingDown) {
    return;
  }
  shuttingDown = true;
  log.info("master", `shutdown ${signal}`);

  server.close(() => {
    wsApi.closeAll();
    if (botManager) {
      botManager.stopAll();
    }
    workers.shutdown();
    process.exit(0);
  });

  setTimeout(() => {
    log.warn("master", "forced exit");
    process.exit(1);
  }, 10000).unref();
}

process.on("SIGINT", () => gracefulShutdown("SIGINT"));
process.on("SIGTERM", () => gracefulShutdown("SIGTERM"));

bootstrap();
