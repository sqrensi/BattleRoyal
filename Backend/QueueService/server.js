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

const metrics = new Metrics();
let databaseDriver = "sqlite";
let shuttingDown = false;
let botManager = null;
let aiBotManager = null;

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

function createTicket(playerId, matchMode, nickname) {
  const ticketId = crypto.randomUUID();
  const ticket = {
    ticketId,
    playerId,
    nickname: nickname || "",
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
        isAiBot: isAiBotTicket(playerA),
        aiProfile: playerA.aiProfile || null,
      },
      {
        ticketId: playerB.ticketId,
        playerId: playerB.playerId,
        nickname: nickB,
        isAiBot: isAiBotTicket(playerB),
        aiProfile: playerB.aiProfile || null,
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

    for (let i = 0; i < queue.length; i++) {
      if (indexA < 0) {
        indexA = i;
        continue;
      }

      if (queuePairCompatible(queue[indexA], queue[i])) {
        indexB = i;
        break;
      }
    }

    if (indexB < 0) {
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
    const aiCount = [a, b].filter((ticket) => isAiBotTicket(ticket)).length;
    const wsBotCount = [a, b].filter((ticket) => isWsBotTicket(ticket)).length;
    log.info(
      "match",
      `created ${match.id} tickets=${a.ticketId.slice(0, 8)},${b.ticketId.slice(0, 8)} ai=${aiCount} wsBots=${wsBotCount} active=${countActiveMatches()} queue=${queue.length}`
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
    if (!ticket || !ticket.playerId || isAiBotTicket(ticket) || isWsBotTicket(ticket)) {
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

function isAiBotTicket(ticket) {
  return String(ticket && ticket.playerId ? ticket.playerId : "").startsWith("ai-bot-player-") ||
    !!(ticket && ticket.isAiBot);
}

function isBotTicket(ticket) {
  return isWsBotTicket(ticket) || isAiBotTicket(ticket);
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

  const aiA = isAiBotTicket(a);
  const aiB = isAiBotTicket(b);
  if (aiA && aiB) {
    return true;
  }
  if (aiA !== aiB) {
    return true;
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
    if (!ticket || !ticket.matchId || isAiBotTicket(ticket)) {
      return false;
    }

    const canBind = ticket.status === "Matched" ||
      (ticket.status === "Queued" && ticket.matchedAtMs && Date.now() - ticket.matchedAtMs < 120000);
    if (!canBind) {
      return false;
    }

    ticket.status = "Matched";
    ticket.socket = socket;
    metrics.setPlayersOnline(Array.from(ticketsById.values()).filter((t) => t.socket).length);
    flushPendingMatchStats(ticketId);
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
    metrics.setPlayersOnline(Array.from(ticketsById.values()).filter((t) => t.socket).length);
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
      if (msg.encoding === "binary") {
        wsApi.sendBinary(ticketId, msg.payload);
      } else {
        wsApi.sendJson(ticketId, msg.payload);
      }
      return;
    }
    for (const id of match.ticketIds) {
      if (msg.encoding === "binary") {
        wsApi.sendBinary(id, msg.payload);
      } else {
        wsApi.sendJson(id, msg.payload);
      }
    }
    return;
  }

  if (msg.type === WorkerToMaster.MATCH_STATE) {
    const match = matchesById.get(msg.matchId);
    if (!match) {
      return;
    }
    if (msg.ticketId) {
      wsApi.sendJson(msg.ticketId, msg.state);
      return;
    }
    for (const ticketId of match.ticketIds) {
      wsApi.sendJson(ticketId, msg.state);
    }
    return;
  }

  if (msg.type === WorkerToMaster.PLAYER_MESSAGE) {
    if (msg.encoding === "binary") {
      wsApi.sendBinary(msg.ticketId, msg.payload);
    } else {
      wsApi.sendJson(msg.ticketId, msg.message);
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
      bots: botManager ? botManager.stats() : null,
      aiBots: aiBotManager ? aiBotManager.stats() : null,
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

  if (aiBotManager && req.method === "POST" && path === "/admin/ai-bots/spawn") {
    const body = await readJsonBody(req);
    const result = await aiBotManager.spawn(body && body.count);
    respondJson(res, 200, { ok: true, ...result });
    return;
  }

  if (aiBotManager && req.method === "POST" && path === "/admin/ai-bots/stop") {
    const result = aiBotManager.stop();
    respondJson(res, 200, { ok: true, ...result });
    return;
  }

  if (aiBotManager && req.method === "GET" && path === "/admin/ai-bots/status") {
    respondJson(res, 200, { ok: true, ...aiBotManager.stats() });
    return;
  }

  if (aiBotManager && req.method === "POST" && path === "/admin/ai-bots/fill-solo") {
    const result = await aiBotManager.fillSoloHumans();
    respondJson(res, 200, { ok: true, ...result });
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

    await playerRepository.ensurePlayer(playerId);
    const nickname = await playerRepository.resolvePlayerNickname(playerId, "");
    const ticket = createTicket(playerId, matchMode, nickname);
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

function initAiBotManager() {
  if (!config.aiBotsEnabled || aiBotManager) {
    return;
  }

  const AiBotManager = require("./bots/aiBotManager");
  aiBotManager = new AiBotManager({
    createTicket,
    enqueueTicket(ticket) {
      queue.push(ticket);
      metrics.setQueueSize(queue.length);
    },
    getQueue: () => queue,
    isHumanTicket,
    isAiBotTicket,
    tryMatchQueue,
    ensurePlayer: (playerId) => playerRepository.ensurePlayer(playerId),
    soloWaitMs: config.aiBotSoloWaitMs,
    autoFillSolo: config.aiBotAutoFillSolo,
  });
}

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
    initAiBotManager();
    if (aiBotManager) {
      aiBotManager.startAutoFill();
      log.info("ai-bots", `enabled soloWaitMs=${config.aiBotSoloWaitMs} thinkHz=${config.aiThinkHz}`);
    }
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
    if (aiBotManager) {
      aiBotManager.stop();
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
