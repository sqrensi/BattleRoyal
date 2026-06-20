const http = require("http");
const { URL } = require("url");
const crypto = require("crypto");
const WebSocket = require("ws");
const { WebSocketServer } = WebSocket;
const { openDatabase } = require("./db/database");
const { registerProfileRoutes } = require("./db/profileRoutes");
const playerRepository = require("./db/playerRepository");

const PORT = toInt(process.env.PORT, 5050);
const MATCH_SERVER_ADDRESS = process.env.MATCH_SERVER_ADDRESS || "127.0.0.1";
const MATCH_SERVER_PORT = toInt(process.env.MATCH_SERVER_PORT, 7777);
const MIN_PLAYERS_TO_MATCH = Math.max(2, toInt(process.env.MIN_PLAYERS_TO_MATCH, 2));
const TARGET_PLAYERS_PER_MATCH = Math.max(2, toInt(process.env.TARGET_PLAYERS_PER_MATCH, 2));
const MATCH_TIMEOUT_SECONDS = Math.max(3, toInt(process.env.MATCH_TIMEOUT_SECONDS, 20));
const MATCH_BATCH_WINDOW_SECONDS = Math.max(0, toInt(process.env.MATCH_BATCH_WINDOW_SECONDS, 2));
const PRESENCE_TIMEOUT_SECONDS = Math.max(2, toInt(process.env.PRESENCE_TIMEOUT_SECONDS, 5));
const MATCH_CONNECT_GRACE_SECONDS = Math.max(5, toInt(process.env.MATCH_CONNECT_GRACE_SECONDS, 45));
const QUEUED_TICKET_TTL_SECONDS = Math.max(5, toInt(process.env.QUEUED_TICKET_TTL_SECONDS, Math.max(MATCH_TIMEOUT_SECONDS, 20)));
const SERVER_TICK_RATE = Math.max(10, toInt(process.env.SERVER_TICK_RATE, 64));
const REALTIME_WS_PORT = toInt(process.env.REALTIME_WS_PORT, 5051);
const DEBUG_REALTIME = (process.env.DEBUG_REALTIME || "0") !== "0";
const DEBUG_MOVEMENT = (process.env.DEBUG_MOVEMENT || "0") !== "0";
const USE_BINARY_POSES = (process.env.USE_BINARY_POSES || "1") !== "0";
const POSE_HISTORY_KEEP_MS = Math.max(200, toInt(process.env.POSE_HISTORY_KEEP_MS, 500));
const SNAPSHOT_HISTORY_SAMPLES = Math.max(4, toInt(process.env.SNAPSHOT_HISTORY_SAMPLES, 16));
const USE_BINARY_SNAPSHOTS = (process.env.USE_BINARY_SNAPSHOTS || "1") !== "0";

try {
  openDatabase();
  console.log("[db] SQLite profile database ready.");
} catch (error) {
  console.error("[db] Failed to initialize SQLite database:", error.message || error);
  process.exit(1);
}

const handleProfileRoutes = registerProfileRoutes({
  readJsonBody,
  respondJson,
  getRequestUrl: (req) => new URL(req.url, `http://${req.headers.host || "127.0.0.1"}`),
});
const killFeedSeqByMatchId = new Map();
const PLAYER_DEATH_CAUSE_PLAYER = "player";
const PLAYER_DEATH_CAUSE_ZONE = "zone";
const MAX_PLAYER_SPEED = Number.isFinite(Number(process.env.MAX_PLAYER_SPEED))
  ? Math.max(4, Number(process.env.MAX_PLAYER_SPEED))
  : 12.5;
const PLAYER_MOVE_SPEED = 5.5;
const PLAYER_SPRINT_MULTIPLIER = 1.55;
const PLAYER_CROUCH_MULTIPLIER = 0.55;
const PLAYER_GRAVITY = -24;
const PLAYER_JUMP_HEIGHT = 1.25;
const PLAYER_SPRINT_MIN_FORWARD = 0.1;
const PLAYER_SIDE_SPEED_MULTIPLIER = 0.85;
const PLAYER_BACKWARD_SPEED_MULTIPLIER = 0.75;
const PLAYER_HIT_RADIUS = Number.isFinite(Number(process.env.PLAYER_HIT_RADIUS))
  ? Math.max(0.4, Number(process.env.PLAYER_HIT_RADIUS))
  : 0.95;
const MAX_WEAPON_RANGE = Number.isFinite(Number(process.env.MAX_WEAPON_RANGE))
  ? Math.max(20, Number(process.env.MAX_WEAPON_RANGE))
  : 150;
const DAMAGE_BY_HIT_ZONE = {
  leg: 15,
  body: 25,
  neck: 80,
  head: 100
};

const ticketsById = new Map();
const queuedTicketIds = [];
let activeMatch = null;
const matchesById = new Map();
let currentServerTick = 0;
const wsClientsByTicketId = new Map();
const wsMetaBySocket = new Map();
const lastPoseDebugByTicket = new Map();
const lastMoveDiagByTicket = new Map();
const lastClientPosePosByTicket = new Map();
const lastSnapshotDebugByOwner = new Map();
const lastMatchSnapshotBroadcastAtMs = new Map();
const matchPickupsByMatchId = new Map();
const matchDamageZonesByMatchId = new Map();
const matchedTicketsByMatchId = new Map();
const droppedWeaponSeqByMatchId = new Map();
const WEAPON_SLOT_EMPTY = 255;
const WEAPON_KIND_MAX = 3;
const WEAPON_MAX_SLOTS = 2;
const PICKUP_MAX_DISTANCE = Number.isFinite(Number(process.env.PICKUP_MAX_DISTANCE))
  ? Math.max(1, Number(process.env.PICKUP_MAX_DISTANCE))
  : 3.5;
const MEDKIT_USE_DURATION_SECONDS = Math.max(0.5, Number(process.env.MEDKIT_USE_DURATION_SECONDS) || 8);
const MEDKIT_HEAL_AMOUNT = Math.max(1, Number(process.env.MEDKIT_HEAL_AMOUNT) || 70);
const MEDKIT_MAX_COUNT = Math.max(1, Math.min(99, Number(process.env.MEDKIT_MAX_COUNT) || 8));
const MEDKIT_STARTING_COUNT = Math.max(0, Math.min(MEDKIT_MAX_COUNT, Number(process.env.MEDKIT_STARTING_COUNT) || 0));
const GRENADE_MAX_COUNT = Math.max(1, Math.min(99, Number(process.env.GRENADE_MAX_COUNT) || 8));
const ZONE_STATE_BROADCAST_INTERVAL_MS = Math.max(100, Number(process.env.ZONE_STATE_BROADCAST_INTERVAL_MS) || 500);
const ZONE_DAMAGE_SEND_INTERVAL_MS = Math.max(50, Number(process.env.ZONE_DAMAGE_SEND_INTERVAL_MS) || 125);
const STALE_TICKET_TTL_MS = Math.max(300000, Number(process.env.STALE_TICKET_TTL_MS) || 3600000);
const ZONE_DEFAULT_INITIAL_RADIUS = Math.max(10, Number(process.env.ZONE_INITIAL_RADIUS) || 220);
const ZONE_DEFAULT_PHASE1_END_RADIUS = Math.max(5, Number(process.env.ZONE_PHASE1_END_RADIUS) || 100);
const ZONE_DEFAULT_FINAL_RADIUS = Math.max(0, Number(process.env.ZONE_FINAL_RADIUS) || 0);
const ZONE_DEFAULT_TOTAL_SHRINK_DURATION_SEC = Math.max(30, Number(process.env.ZONE_TOTAL_SHRINK_DURATION_SEC) || 180);
const ZONE_DEFAULT_PHASE1_CENTER_OFFSET = Math.max(0, Number(process.env.ZONE_PHASE1_CENTER_OFFSET) || 10);
const ZONE_DEFAULT_PHASE2_CENTER_OFFSET = Math.max(0, Number(process.env.ZONE_PHASE2_CENTER_OFFSET) || 40);
const ZONE_DEFAULT_DAMAGE_MIN_DPS = Math.max(0, Number(process.env.ZONE_DAMAGE_MIN_DPS) || 0.5);
const ZONE_DEFAULT_DAMAGE_MAX_DPS = Math.max(ZONE_DEFAULT_DAMAGE_MIN_DPS, Number(process.env.ZONE_DAMAGE_MAX_DPS) || 14);
const ZONE_DEFAULT_DAMAGE_RAMP_SEC = Math.max(30, Number(process.env.ZONE_DAMAGE_RAMP_SEC) || 480);
const BR_LOBBY_COUNTDOWN_SECONDS = Math.max(3, toInt(process.env.BR_LOBBY_COUNTDOWN_SECONDS, 10));
const BR_PLANE_DURATION_SECONDS = Math.max(10, toInt(process.env.BR_PLANE_DURATION_SECONDS, 45));
const BR_PLANE_ALTITUDE = Math.max(20, Number(process.env.BR_PLANE_ALTITUDE) || 120);
const BR_PLANE_HALF_LENGTH = Math.max(100, Number(process.env.BR_PLANE_HALF_LENGTH) || 200);
const BR_PLANE_START_RADIUS = Math.max(40, Number(process.env.BR_PLANE_START_RADIUS) || 60);
const BR_PLANE_SPEED = Math.max(5, Number(process.env.BR_PLANE_SPEED) || 80);
const BR_DEATH_DISCONNECT_SECONDS = Math.max(1, toInt(process.env.BR_DEATH_DISCONNECT_SECONDS, 5));
const BR_WINNER_DISCONNECT_SECONDS = Math.max(3, toInt(process.env.BR_WINNER_DISCONNECT_SECONDS, 10));
const BR_PLANE_SPAWN_SLOTS = Math.max(1, toInt(process.env.BR_PLANE_SPAWN_SLOTS, 12));
const BR_DROP_MAX_DISTANCE_FROM_CENTER = Math.max(10, Number(process.env.BR_DROP_MAX_DISTANCE_FROM_CENTER) || 60);
const BR_DEATH_FALL_LAND_TIMEOUT_SECONDS = Math.max(2, toInt(process.env.BR_DEATH_FALL_LAND_TIMEOUT_SECONDS, 8));
const BR_MATCH_STATE_BROADCAST_MS = Math.max(100, toInt(process.env.BR_MATCH_STATE_BROADCAST_MS, 250));

setInterval(() => {
  currentServerTick += 1;
  tickAllPlayerMovement();
  tickMedkitUses();
  tickPickupRespawns();
  tickDamageZones();
  runMaintenanceSweep();
  broadcastRealtimeSnapshots();
}, Math.max(1, Math.floor(1000 / SERVER_TICK_RATE)));

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
      queueSize: queuedTicketIds.length,
      ticketCount: ticketsById.size,
      activeMatchId: activeMatch ? activeMatch.matchId : "",
      activeSessionCount: Array.from(matchesById.values()).filter((m) => m.state !== "Ended").length,
      database: "sqlite"
    });
    return;
  }

  if (await handleProfileRoutes(req, res, path, req.method)) {
    return;
  }

  if (req.method === "GET" && path === "/telemetry/active") {
    respondJson(res, 200, {
      serverTick: currentServerTick,
      queueSize: queuedTicketIds.length,
      activeMatchId: activeMatch ? activeMatch.matchId : "",
      sessions: Array.from(matchesById.values()).map(toSerializableSession)
    });
    return;
  }

  if (req.method === "GET" && path.startsWith("/telemetry/match/")) {
    const matchId = decodeURIComponent(path.replace("/telemetry/match/", ""));
    const session = matchesById.get(matchId);
    if (!session) {
      respondJson(res, 404, { error: "MatchNotFound", matchId });
      return;
    }

    respondJson(res, 200, {
      serverTick: currentServerTick,
      match: toSerializableSession(session),
      players: buildMatchTelemetryPlayers(matchId)
    });
    return;
  }

  if (req.method === "POST" && path === "/enqueue") {
    const body = await readJsonBody(req);
    const playerId = normalizePlayerId(body && body.playerId);
    playerRepository.ensurePlayer(playerId);
    const ticket = createQueuedTicket(playerId, body && body.matchMode);
    syncTicketNickname(ticket);
    if (DEBUG_REALTIME) {
      console.log(`[http][enqueue] player=${playerId} ticket=${ticket.ticketId} mode=${ticket.matchMode}`);
    }

    if (ticket.matchMode === "training") {
      removeFromQueue(ticket.ticketId);
      const match = getOrCreateActiveMatch();
      match.matchMode = "training";
      matchTicketToSession(ticket, match, Date.now());
      synchronizeActiveMatchCounts();
    } else {
      tryMatchTickets();
    }

    respondJson(res, 200, {
      ticketId: ticket.ticketId,
      status: ticket.status
    });
    return;
  }

  if (req.method === "POST" && path === "/dequeue") {
    const body = await readJsonBody(req);
    const ticketId = body && body.ticketId;
    if (!ticketId || !ticketsById.has(ticketId)) {
      respondJson(res, 404, {
        success: false,
        status: "NotFound"
      });
      return;
    }

    const ticket = ticketsById.get(ticketId);
    if (ticket.status === "Queued") {
      ticket.status = "Cancelled";
      removeFromQueue(ticket.ticketId);
    }

    respondJson(res, 200, {
      success: true,
      status: ticket.status
    });
    return;
  }

  if (req.method === "POST" && path === "/match/leave") {
    const body = await readJsonBody(req);
    const ticketId = body && body.ticketId;
    if (!ticketId || !ticketsById.has(ticketId)) {
      respondJson(res, 404, {
        success: false,
        status: "NotFound"
      });
      return;
    }

    const ticket = ticketsById.get(ticketId);
    if (ticket.status === "Matched" || ticket.status === "Disconnected") {
      handleBattleRoyalePlayerLeft(ticketId);
      ticket.status = "Left";
      removeFromActiveMatch(ticket.ticketId);
      synchronizeActiveMatchCounts();
    }

    respondJson(res, 200, {
      success: true,
      status: ticket.status
    });
    return;
  }

  if (req.method === "GET" && path.startsWith("/ticket/")) {
    const ticketId = decodeURIComponent(path.replace("/ticket/", ""));
    const ticket = ticketsById.get(ticketId);
    if (!ticket) {
      respondJson(res, 404, {
        ticketId,
        status: "Expired"
      });
      return;
    }

    expireTicketIfNeeded(ticket);
    tryMatchTickets();

    respondJson(res, 200, toTicketStatus(ticket));
    if (DEBUG_REALTIME) {
      console.log(`[http][ticket] id=${ticketId} status=${ticket.status} match=${ticket.matchId || "none"} players=${ticket.matchedPlayerCount || 0}`);
    }
    return;
  }

  if (req.method === "POST" && path === "/match/presence/update") {
    const body = await readJsonBody(req);
    const ticketId = body && body.ticketId;
    const ticket = ticketId ? ticketsById.get(ticketId) : null;
    if (!ticket || ticket.status !== "Matched") {
      respondJson(res, 404, { success: false, status: "NotMatched" });
      return;
    }

    const position = normalizePosition(body && body.position);
    const yaw = normalizeNumber(body && body.yaw, 0);
    const sampleTimeMs = normalizeInt64(body && body.sampleTimeMs, Date.now());
    const serverSampleTimeMs = Date.now();
    ticket.presence = {
      position,
      yaw,
      sampleTick: currentServerTick,
      sampleTimeMs,
      serverSampleTimeMs,
      lastSeenMs: serverSampleTimeMs
    };

    respondJson(res, 200, { success: true });
    return;
  }

  if (req.method === "POST" && path === "/match/presence/sync") {
    const body = await readJsonBody(req);
    const ticketId = body && body.ticketId;
    const ticket = ticketId ? ticketsById.get(ticketId) : null;
    if (!ticket || ticket.status !== "Matched") {
      respondJson(res, 404, { success: false, status: "NotMatched", serverTimeMs: Date.now(), players: [] });
      return;
    }

    const position = normalizePosition(body && body.position);
    const yaw = normalizeNumber(body && body.yaw, 0);
    const sampleTimeMs = normalizeInt64(body && body.sampleTimeMs, Date.now());
    const serverSampleTimeMs = Date.now();
    ticket.presence = {
      position,
      yaw,
      sampleTick: currentServerTick,
      sampleTimeMs,
      serverSampleTimeMs,
      lastSeenMs: serverSampleTimeMs
    };

    const players = collectMatchPresence(ticket);
    respondJson(res, 200, { success: true, serverTimeMs: Date.now(), serverTick: currentServerTick, serverTickRate: SERVER_TICK_RATE, players });
    return;
  }

  if (req.method === "GET" && path.startsWith("/match/presence/")) {
    const ticketId = decodeURIComponent(path.replace("/match/presence/", ""));
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Matched") {
      respondJson(res, 404, { success: false, status: "NotMatched", players: [] });
      return;
    }

    const players = collectMatchPresence(ticket);
    respondJson(res, 200, { success: true, serverTimeMs: Date.now(), serverTick: currentServerTick, serverTickRate: SERVER_TICK_RATE, players });
    return;
  }

  respondJson(res, 404, { error: "NotFound" });
});

server.listen(PORT, "0.0.0.0", () => {
  console.log(`[QueueService] listening on http://127.0.0.1:${PORT}`);
  console.log(
    `[QueueService] match endpoint -> ${MATCH_SERVER_ADDRESS}:${MATCH_SERVER_PORT}, minPlayers=${MIN_PLAYERS_TO_MATCH}, timeout=${MATCH_TIMEOUT_SECONDS}s, batchWindow=${MATCH_BATCH_WINDOW_SECONDS}s`
  );
});

const wsServer = new WebSocketServer({ port: REALTIME_WS_PORT });
wsServer.on("listening", () => {
  console.log(`[QueueService] realtime websocket listening on ws://127.0.0.1:${REALTIME_WS_PORT}`);
});
wsServer.on("error", (err) => {
  console.error(`[QueueService] websocket server failed on port ${REALTIME_WS_PORT}: ${err && err.message ? err.message : err}`);
  if (err && err.code === "EADDRINUSE") {
    console.error("[QueueService] port is busy — stop the old QueueService (tools/stop_local_stack.ps1) before starting again.");
  }
  process.exit(1);
});
wsServer.on("connection", (socket) => {
  if (DEBUG_REALTIME) {
    console.log("[rt][ws-connection] open");
  }
  wsMetaBySocket.set(socket, { ticketId: "" });

  socket.on("message", (raw) => {
    if (Buffer.isBuffer(raw) && raw.length >= 4 &&
        raw[0] === 0x52 && raw[1] === 0x54 && raw[2] === 0x50 && raw[3] === 0x31) {
      try {
        const poseMessage = decodeBinaryPose(raw);
        if (poseMessage) {
          handleWsPose(socket, poseMessage);
        } else if (DEBUG_REALTIME) {
          console.warn(`[rt][pose-binary-decode-null] len=${raw.length}`);
        }
      } catch (err) {
        if (DEBUG_REALTIME) {
          console.error("[rt][pose-binary-decode-fail]", err);
        }
      }
      return;
    }

    let message;
    try {
      message = JSON.parse(raw.toString("utf8"));
    } catch {
      if (DEBUG_REALTIME) {
        console.log("[rt][message-parse-error]");
      }
      return;
    }

    if (!message || typeof message.type !== "string") {
      return;
    }

    try {
      if (message.type === "join") {
        const ticketId = typeof message.ticketId === "string" ? message.ticketId : "";
        handleWsJoin(socket, ticketId);
        return;
      }

      if (message.type === "pose") {
        handleWsPose(socket, message);
        return;
      }

      if (message.type === "shot") {
        handleWsShot(socket, message);
        return;
      }

      if (message.type === "hit") {
        handleWsHit(socket, message);
        return;
      }

      if (message.type === "register_pickups") {
        handleWsRegisterPickups(socket, message);
        return;
      }

      if (message.type === "register_damage_zone") {
        handleWsRegisterDamageZone(socket, message);
        return;
      }

      if (message.type === "pickup") {
        handleWsPickup(socket, message);
        return;
      }

      if (message.type === "weapon_drop") {
        handleWsWeaponDrop(socket, message);
        return;
      }

      if (message.type === "weapon_swap") {
        handleWsWeaponSwap(socket, message);
        return;
      }

      if (message.type === "inventory_item_drop") {
        handleWsInventoryItemDrop(socket, message);
        return;
      }

      if (message.type === "ammo_state") {
        handleWsAmmoState(socket, message);
        return;
      }

      if (message.type === "medkit_use") {
        handleWsMedkitUse(socket, message);
        return;
      }

      if (message.type === "medkit_cancel") {
        handleWsMedkitCancel(socket);
        return;
      }

      if (message.type === "plane_jump") {
        handleWsPlaneJump(socket);
        return;
      }

      if (message.type === "plane_landed") {
        handleWsPlaneLanded(socket);
        return;
      }

      if (message.type === "player_land") {
        handleWsPlayerLand(socket);
        return;
      }

      if (message.type === "death_fall_landed") {
        handleWsDeathFallLanded(socket);
        return;
      }

      if (message.type === "ping") {
        socket.send(JSON.stringify({
          type: "pong",
          clientTimeMs: normalizeInt64(message.clientTimeMs, 0)
        }));
        return;
      }

      if (DEBUG_REALTIME) {
        console.log(`[rt][message-unknown] type=${String(message.type)}`);
      }
    } catch (err) {
      console.error(`[QueueService][ws] handler failed type=${message.type}:`, err);
    }
  });

  socket.on("close", () => {
    if (DEBUG_REALTIME) {
      console.log("[rt][ws-connection] close");
    }
    handleWsDisconnect(socket);
  });

  socket.on("error", () => {
    if (DEBUG_REALTIME) {
      console.log("[rt][ws-connection] error");
    }
    handleWsDisconnect(socket);
  });
});

function syncTicketNickname(ticket) {
  if (!ticket) {
    return;
  }

  ticket.nickname = playerRepository.resolvePlayerNickname(ticket.playerId, ticket.ticketId);
  if (ticket.presence) {
    ticket.presence.nickname = ticket.nickname;
  }
}

function resolveTicketNickname(ticket) {
  if (!ticket) {
    return "Игрок";
  }

  if (ticket.nickname) {
    return ticket.nickname;
  }

  syncTicketNickname(ticket);
  return ticket.nickname || "Игрок";
}

function markTicketDeathCause(ticket, cause) {
  if (!ticket || !cause) {
    return;
  }

  ticket.deathCause = cause;
}

function wasKilledByPlayer(ticket) {
  return !!ticket && ticket.deathCause === PLAYER_DEATH_CAUSE_PLAYER;
}

function shouldLockBattleRoyaleDeathState(ticket, prevWasDead) {
  if (!ticket || !isBattleRoyaleRespawnBlocked(ticket)) {
    return false;
  }

  return !!prevWasDead || !!ticket.deathCause;
}

function broadcastKillFeed(matchId, payload) {
  if (!matchId) {
    return;
  }

  const nextSeq = (killFeedSeqByMatchId.get(matchId) || 0) + 1;
  killFeedSeqByMatchId.set(matchId, nextSeq);
  const message = JSON.stringify({
    type: "kill_feed",
    seq: nextSeq,
    ...payload,
  });

  for (const [ticketId, socket] of wsClientsByTicketId.entries()) {
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.matchId !== matchId || socket.readyState !== WebSocket.OPEN) {
      continue;
    }

    try {
      socket.send(message);
    } catch {
      // ignored
    }
  }
}

function normalizeMatchMode(value) {
  const raw = String(value || "battle_royale").trim().toLowerCase();
  if (raw === "training") {
    return "training";
  }
  if (raw === "duel" || raw === "1v1" || raw === "duel_1v1") {
    return "duel";
  }
  return "battle_royale";
}

function getMatchModeConfig(matchMode) {
  const normalized = normalizeMatchMode(matchMode);
  if (normalized === "training") {
    return { minPlayers: 1, targetPlayers: 1 };
  }
  if (normalized === "duel") {
    return { minPlayers: 2, targetPlayers: 2 };
  }
  return {
    minPlayers: Math.max(MIN_PLAYERS_TO_MATCH, TARGET_PLAYERS_PER_MATCH),
    targetPlayers: TARGET_PLAYERS_PER_MATCH,
  };
}

function createQueuedTicket(playerId, matchMode) {
  cancelExistingQueuedTicketsForPlayer(playerId);
  removeDisconnectedMatchedTicketsForPlayer(playerId);

  const ticketId = crypto.randomUUID();
  const ticket = {
    ticketId,
    playerId,
    status: "Queued",
    matchMode: normalizeMatchMode(matchMode),
    queueEnterTimeMs: Date.now(),
    matchedAtMs: 0,
    matchId: "",
    matchedPlayerCount: 0,
    presence: null,
    telemetry: {
      wsOpenCount: 0,
      wsCloseCount: 0,
      reconnectCount: 0,
      poseReceived: 0,
      poseMissing: 0,
      poseOutOfOrder: 0,
      lastPoseSeq: -1,
      lastWsOpenMs: 0,
      lastWsCloseMs: 0
    },
    serverAddress: "",
    serverPort: 0,
    deathCause: "",
  };

  ticketsById.set(ticketId, ticket);
  queuedTicketIds.push(ticketId);
  return ticket;
}

function tryMatchTickets() {
  pruneQueuedTickets();

  if (queuedTicketIds.length === 0) {
    return;
  }

  const nowMs = Date.now();
  assignQueuedTicketsToExistingSessions(nowMs);

  if (queuedTicketIds.length === 0) {
    return;
  }

  tryMatchModeBucket("duel", nowMs);
  tryMatchModeBucket("battle_royale", nowMs);
}

function tryMatchModeBucket(matchMode, nowMs) {
  const config = getMatchModeConfig(matchMode);
  const bucketIds = queuedTicketIds.filter((ticketId) => {
    const ticket = ticketsById.get(ticketId);
    return ticket && ticket.status === "Queued" && normalizeMatchMode(ticket.matchMode) === matchMode;
  });

  if (bucketIds.length < config.minPlayers) {
    return;
  }

  const oldestTicket = ticketsById.get(bucketIds[0]);
  if (!oldestTicket) {
    return;
  }

  const oldestWaitMs = nowMs - oldestTicket.queueEnterTimeMs;
  const batchWindowElapsed = oldestWaitMs >= MATCH_BATCH_WINDOW_SECONDS * 1000;
  const shouldMatchByTimeout = oldestWaitMs >= MATCH_TIMEOUT_SECONDS * 1000;
  if (!batchWindowElapsed && !shouldMatchByTimeout && bucketIds.length < config.targetPlayers) {
    return;
  }

  const matchSize = Math.min(config.targetPlayers, bucketIds.length);
  const matchedTickets = [];
  for (let i = 0; i < matchSize; i++) {
    const ticketId = bucketIds[i];
    removeFromQueue(ticketId);
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Queued") {
      continue;
    }

    matchedTickets.push(ticket);
  }

  if (matchedTickets.length === 0) {
    return;
  }

  const match = getOrCreateActiveMatch();
  match.matchMode = matchMode;
  for (const ticket of matchedTickets) {
    matchTicketToSession(ticket, match, nowMs);
  }

  synchronizeActiveMatchCounts();
}

function expireTicketIfNeeded(ticket) {
  if (!ticket || ticket.status !== "Queued") {
    return;
  }

  const queueAgeMs = Date.now() - ticket.queueEnterTimeMs;
  if (queueAgeMs > QUEUED_TICKET_TTL_SECONDS * 1000) {
    ticket.status = "Expired";
    removeFromQueue(ticket.ticketId);
  }
}

function removeFromQueue(ticketId) {
  const idx = queuedTicketIds.indexOf(ticketId);
  if (idx >= 0) {
    queuedTicketIds.splice(idx, 1);
  }
}

function toTicketStatus(ticket) {
  synchronizeActiveMatchCounts();

  let normalizedStatus = ticket.status;
  if (
    normalizedStatus === "Disconnected" ||
    normalizedStatus === "Left" ||
    normalizedStatus === "NotMatched")
  {
    normalizedStatus = "Expired";
  }

  return {
    ticketId: ticket.ticketId,
    playerId: ticket.playerId,
    status: normalizedStatus,
    queueDurationSeconds: (Date.now() - ticket.queueEnterTimeMs) / 1000,
    matchId: ticket.matchId || "",
    matchedPlayerCount: ticket.matchedPlayerCount || 0,
    serverAddress: ticket.serverAddress || "",
    serverPort: ticket.serverPort || 0
  };
}

function collectMatchPresence(ownerTicket) {
  synchronizeActiveMatchCounts();
  if (!ownerTicket || !ownerTicket.matchId) {
    return [];
  }
  return collectRealtimePlayersForMatch(ownerTicket.matchId, ownerTicket.ticketId);
}

function createBattleRoyaleState() {
  return {
    phase: "lobby",
    joinLocked: false,
    countdownEndsAtMs: 0,
    planeStartedAtMs: 0,
    planeEndsAtMs: 0,
    playingStartedAtMs: 0,
    endingStartedAtMs: 0,
    winnerTicketId: "",
    mapCenterX: 0,
    mapCenterZ: 0,
    planeStartX: -BR_PLANE_START_RADIUS,
    planeStartZ: 0,
    planeEndX: BR_PLANE_START_RADIUS,
    planeEndZ: 0,
    planeY: BR_PLANE_ALTITUDE,
    planeSpeed: BR_PLANE_SPEED,
    connectedTickets: new Set(),
    aliveTickets: new Set(),
    jumpedTickets: new Set(),
    voluntaryJumpTickets: new Set(),
    landedTickets: new Set(),
    dropPositionsByTicket: new Map(),
    eliminatedTickets: new Set(),
    disconnectAtByTicket: new Map(),
    killCountByTicket: new Map(),
    deathFallPendingTickets: new Set(),
    deathFallPendingSinceByTicket: new Map(),
    pendingWinnerTicketId: "",
    planePathAngle: 0,
    planePathInitialized: false,
    planeSpawnIndexByTicket: new Map(),
    lastStateBroadcastMs: 0
  };
}

function ensureBattleRoyaleState(session) {
  if (!session) {
    return null;
  }
  if (!session.br) {
    session.br = createBattleRoyaleState();
  }
  return session.br;
}

function getOrCreateActiveMatch() {
  const nowMs = Date.now();
  const session = {
    matchId: crypto.randomUUID(),
    createdAtMs: nowMs,
    startedAtMs: nowMs,
    endedAtMs: 0,
    lastActivityMs: nowMs,
    state: "Open",
    matchMode: "battle_royale",
    ticketIds: new Set(),
    br: createBattleRoyaleState()
  };
  matchesById.set(session.matchId, session);
  activeMatch = session;
  return session;
}

function removeFromActiveMatch(ticketId) {
  const ticket = ticketsById.get(ticketId);
  const session = ticket && ticket.matchId ? matchesById.get(ticket.matchId) : activeMatch;
  if (!session) {
    return;
  }

  session.ticketIds.delete(ticketId);
  const socket = wsClientsByTicketId.get(ticketId);
  if (socket) {
    safeWsClose(socket, 1000, "removed from match");
    wsClientsByTicketId.delete(ticketId);
    wsMetaBySocket.delete(socket);
  }
  if (session.ticketIds.size === 0) {
    session.state = "Ended";
    session.endedAtMs = Date.now();
    if (activeMatch && activeMatch.matchId === session.matchId) {
      activeMatch = null;
    }
    matchDamageZonesByMatchId.delete(session.matchId);
    matchedTicketsByMatchId.delete(session.matchId);
  }
}

function assignQueuedTicketsToExistingSessions(nowMs) {
  if (queuedTicketIds.length === 0) {
    return;
  }

  for (let i = queuedTicketIds.length - 1; i >= 0; i--) {
    const ticketId = queuedTicketIds[i];
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Queued") {
      queuedTicketIds.splice(i, 1);
      continue;
    }

    const joinableSessions = getJoinableSessions(normalizeMatchMode(ticket.matchMode));
    if (joinableSessions.length === 0) {
      continue;
    }

    const selected = joinableSessions[Math.floor(Math.random() * joinableSessions.length)];
    queuedTicketIds.splice(i, 1);
    matchTicketToSession(ticket, selected, nowMs);
  }
}

function getJoinableSessions(matchMode) {
  const normalized = normalizeMatchMode(matchMode);
  const config = getMatchModeConfig(normalized);
  const sessions = [];
  for (const session of matchesById.values()) {
    if (!session || session.state === "Ended") {
      continue;
    }

    if (normalizeMatchMode(session.matchMode) !== normalized) {
      continue;
    }

    let matchedCount = 0;
    for (const ticketId of session.ticketIds) {
      const ticket = ticketsById.get(ticketId);
      if (ticket && ticket.status === "Matched") {
        matchedCount++;
      }
    }

    if (matchedCount >= 1 && matchedCount < config.targetPlayers) {
      const br = ensureBattleRoyaleState(session);
      if (br && br.joinLocked) {
        continue;
      }
      if (br && br.phase !== "lobby" && br.phase !== "countdown") {
        continue;
      }
      sessions.push(session);
    }
  }
  return sessions;
}

function matchTicketToSession(ticket, session, nowMs) {
  if (!ticket || !session) {
    return;
  }

  ticket.status = "Matched";
  ticket.matchedAtMs = nowMs;
  ticket.matchId = session.matchId;
  ticket.deathCause = "";
  ticket.serverAddress = MATCH_SERVER_ADDRESS;
  ticket.serverPort = MATCH_SERVER_PORT;
  session.ticketIds.add(ticket.ticketId);
  session.lastActivityMs = nowMs;
  if (!session.matchMode) {
    session.matchMode = normalizeMatchMode(ticket.matchMode);
  }
  if (session.state === "Open") {
    session.state = "Active";
  }

  if (ticket.presence == null) {
    ticket.presence = createDefaultPresence(0, nowMs);
  }

  addTicketToMatchIndex(ticket);
}

function ensureMatchTicketSet(matchId) {
  if (!matchId) {
    return null;
  }

  if (!matchedTicketsByMatchId.has(matchId)) {
    matchedTicketsByMatchId.set(matchId, new Set());
  }

  return matchedTicketsByMatchId.get(matchId);
}

function addTicketToMatchIndex(ticket) {
  if (!ticket || ticket.status !== "Matched" || !ticket.matchId) {
    return;
  }

  ensureMatchTicketSet(ticket.matchId).add(ticket.ticketId);
}

function removeTicketFromMatchIndex(ticket) {
  if (!ticket || !ticket.matchId) {
    return;
  }

  const ticketIds = matchedTicketsByMatchId.get(ticket.matchId);
  if (!ticketIds) {
    return;
  }

  ticketIds.delete(ticket.ticketId);
  if (ticketIds.size === 0) {
    matchedTicketsByMatchId.delete(ticket.matchId);
  }
}

function getMatchedTicketsForMatch(matchId) {
  if (!matchId) {
    return [];
  }

  const ticketIds = matchedTicketsByMatchId.get(matchId);
  if (!ticketIds || ticketIds.size === 0) {
    return [];
  }

  const results = [];
  for (const ticketId of ticketIds) {
    const ticket = ticketsById.get(ticketId);
    if (ticket && ticket.status === "Matched" && ticket.matchId === matchId) {
      results.push(ticket);
      continue;
    }

    ticketIds.delete(ticketId);
  }

  if (ticketIds.size === 0) {
    matchedTicketsByMatchId.delete(matchId);
  }

  return results;
}

function handleWsJoin(socket, ticketId) {
  if (!ticketId || !ticketsById.has(ticketId)) {
    safeWsClose(socket, 1008, "invalid ticket");
    return;
  }

  const ticket = ticketsById.get(ticketId);
  if (!ticket || ticket.status !== "Matched") {
    safeWsClose(socket, 1008, "ticket not matched");
    return;
  }

  const session = ticket.matchId ? matchesById.get(ticket.matchId) : null;
  const br = ensureBattleRoyaleState(session);
  if (br && br.joinLocked) {
    safeWsClose(socket, 1008, "match_started");
    return;
  }

  const previous = wsClientsByTicketId.get(ticketId);
  if (previous && previous !== socket) {
    safeWsClose(previous, 1000, "replaced by newer connection");
  }

  const nowMs = Date.now();
  if (!ticket.presence) {
    ticket.presence = createDefaultPresence(currentServerTick, nowMs);
  } else {
    ticket.presence.sampleTick = ticket.presence.sampleTick || currentServerTick;
    ticket.presence.sampleTimeMs = ticket.presence.sampleTimeMs || nowMs;
    ticket.presence.serverSampleTimeMs = nowMs;
    ticket.presence.lastSeenMs = nowMs;
  }

  syncTicketNickname(ticket);

  wsClientsByTicketId.set(ticketId, socket);
  wsMetaBySocket.set(socket, { ticketId });
  touchMatchSession(ticket.matchId);
  const joinTelemetry = ensureTicketTelemetry(ticket);
  joinTelemetry.wsOpenCount += 1;
  joinTelemetry.reconnectCount = Math.max(0, joinTelemetry.wsOpenCount - 1);
  joinTelemetry.lastWsOpenMs = Date.now();
  if (DEBUG_REALTIME) {
    console.log(`[rt][join] ticket=${ticketId} player=${ticket.playerId} match=${ticket.matchId || "none"}`);
  }

  try {
    socket.send(JSON.stringify({
      type: "joined",
      ticketId
    }));
    sendSnapshotToSocket(ticketId, socket);
    if (ticket.matchId) {
      sendPickupStateToSocket(socket, ticket.matchId);
      sendZoneStateToSocket(socket, ticket.matchId);
      if (br) {
        br.connectedTickets.add(ticketId);
        if (!br.eliminatedTickets.has(ticketId)) {
          br.aliveTickets.add(ticketId);
        }
        maybeStartBattleRoyaleCountdown(session);
        sendMatchStateToSocket(socket, session);
      }
    }
  } catch {
    safeWsClose(socket, 1011, "failed to send join ack");
  }
}

function resolveWeaponKindFromItemId(itemId) {
  const id = typeof itemId === "string" ? itemId.toLowerCase() : "";
  if (id.includes("sniper")) {
    return 1;
  }
  if (id.includes("pistol")) {
    return 2;
  }
  if (id.includes("mp7") || id.includes("ppsh")) {
    return 3;
  }
  return 0;
}

function resolveMagazineSizeForKind(kind) {
  const normalized = normalizeInt64(kind, 0);
  if (normalized === 1) {
    return 7;
  }
  if (normalized === 2) {
    return 12;
  }
  if (normalized === 3) {
    return 30;
  }
  return 30;
}

function isWeaponSlotOccupied(kind) {
  return kind !== WEAPON_SLOT_EMPTY && kind >= 0 && kind <= WEAPON_KIND_MAX;
}

function countOccupiedWeaponSlots(presence) {
  if (!presence) {
    return 0;
  }

  let count = 0;
  if (isWeaponSlotOccupied(presence.weaponSlot0Kind)) {
    count += 1;
  }
  if (isWeaponSlotOccupied(presence.weaponSlot1Kind)) {
    count += 1;
  }
  return count;
}

function syncWeaponPresenceFlags(presence) {
  if (!presence) {
    return;
  }

  presence.hasWeapon = countOccupiedWeaponSlots(presence) > 0;
  if (!presence.hasWeapon) {
    presence.weaponKind = 0;
    return;
  }

  const activeSlot = presence.activeWeaponSlot;
  if (activeSlot === 0 || activeSlot === 1) {
    const activeKind = getWeaponSlotKind(presence, activeSlot);
    if (isWeaponSlotOccupied(activeKind)) {
      presence.weaponKind = activeKind;
      return;
    }
  }

  if (isWeaponSlotOccupied(presence.weaponSlot0Kind)) {
    presence.weaponKind = presence.weaponSlot0Kind;
    return;
  }

  if (isWeaponSlotOccupied(presence.weaponSlot1Kind)) {
    presence.weaponKind = presence.weaponSlot1Kind;
  }
}

function getWeaponSlotKind(presence, slotIndex) {
  return slotIndex === 0 ? presence.weaponSlot0Kind : presence.weaponSlot1Kind;
}

function getWeaponSlotItemId(presence, slotIndex) {
  return slotIndex === 0 ? (presence.weaponSlot0ItemId || "") : (presence.weaponSlot1ItemId || "");
}

function setWeaponSlot(presence, slotIndex, itemId, kind) {
  if (slotIndex === 0) {
    presence.weaponSlot0Kind = kind;
    presence.weaponSlot0ItemId = itemId || "";
  } else {
    presence.weaponSlot1Kind = kind;
    presence.weaponSlot1ItemId = itemId || "";
  }
}

function clearWeaponSlot(presence, slotIndex) {
  setWeaponSlot(presence, slotIndex, "", WEAPON_SLOT_EMPTY);
  setWeaponSlotMagAmmo(presence, slotIndex, -1);
}

function getWeaponSlotMagAmmo(presence, slotIndex) {
  if (slotIndex === 0) {
    return Number.isFinite(presence.weaponSlot0MagAmmo) ? presence.weaponSlot0MagAmmo : -1;
  }

  if (slotIndex === 1) {
    return Number.isFinite(presence.weaponSlot1MagAmmo) ? presence.weaponSlot1MagAmmo : -1;
  }

  return -1;
}

function setWeaponSlotMagAmmo(presence, slotIndex, magAmmo) {
  const normalized = Number.isFinite(magAmmo) ? Math.max(-1, Math.min(999, magAmmo)) : -1;
  if (slotIndex === 0) {
    presence.weaponSlot0MagAmmo = normalized;
    return;
  }

  if (slotIndex === 1) {
    presence.weaponSlot1MagAmmo = normalized;
  }
}

function ensureSpareAmmoByKind(presence) {
  if (!presence) {
    return;
  }

  if (!Array.isArray(presence.spareAmmoByKind) || presence.spareAmmoByKind.length < WEAPON_KIND_MAX + 1) {
    const legacy = Number.isFinite(presence.spareAmmo) ? Math.max(0, Math.min(999, presence.spareAmmo)) : 0;
    presence.spareAmmoByKind = [legacy, 0, 0, 0];
    while (presence.spareAmmoByKind.length < WEAPON_KIND_MAX + 1) {
      presence.spareAmmoByKind.push(0);
    }
  }
}

function getSpareAmmoForKind(presence, kind) {
  ensureSpareAmmoByKind(presence);
  const normalized = normalizeInt64(kind, 0);
  if (normalized < 0 || normalized > WEAPON_KIND_MAX) {
    return 0;
  }

  const value = presence.spareAmmoByKind[normalized];
  return Number.isFinite(value) ? Math.max(0, Math.min(999, value)) : 0;
}

function setSpareAmmoForKind(presence, kind, spareAmmo) {
  ensureSpareAmmoByKind(presence);
  const normalized = normalizeInt64(kind, 0);
  if (normalized < 0 || normalized > WEAPON_KIND_MAX) {
    return;
  }

  presence.spareAmmoByKind[normalized] = Number.isFinite(spareAmmo)
    ? Math.max(0, Math.min(999, spareAmmo))
    : 0;
}

function getSpareAmmo(presence) {
  if (!presence) {
    return 0;
  }

  const activeSlot = presence.activeWeaponSlot;
  if (activeSlot === 0 || activeSlot === 1) {
    const activeKind = getWeaponSlotKind(presence, activeSlot);
    if (isWeaponSlotOccupied(activeKind)) {
      return getSpareAmmoForKind(presence, activeKind);
    }
  }

  if (isWeaponSlotOccupied(presence.weaponSlot0Kind)) {
    return getSpareAmmoForKind(presence, presence.weaponSlot0Kind);
  }

  if (isWeaponSlotOccupied(presence.weaponSlot1Kind)) {
    return getSpareAmmoForKind(presence, presence.weaponSlot1Kind);
  }

  return getSpareAmmoForKind(presence, 0);
}

function setSpareAmmo(presence, spareAmmo) {
  if (!presence) {
    return;
  }

  const activeSlot = presence.activeWeaponSlot;
  if (activeSlot === 0 || activeSlot === 1) {
    const activeKind = getWeaponSlotKind(presence, activeSlot);
    if (isWeaponSlotOccupied(activeKind)) {
      setSpareAmmoForKind(presence, activeKind, spareAmmo);
      return;
    }
  }

  setSpareAmmoForKind(presence, 0, spareAmmo);
}

function resolveAmmoKindFromItemId(itemId) {
  const id = typeof itemId === "string" ? itemId.toLowerCase() : "";
  if (id.includes("sniper")) {
    return 1;
  }
  if (id.includes("pistol")) {
    return 2;
  }
  if (id.includes("mp7") || id.includes("ppsh")) {
    return 3;
  }
  if (id.includes("assault") || id.includes("ammo_pack") || id.includes("rifle")) {
    return 0;
  }
  return -1;
}

function buildSpareAmmoPayload(presence) {
  ensureSpareAmmoByKind(presence);
  return {
    spareAmmoAssault: getSpareAmmoForKind(presence, 0),
    spareAmmoSniper: getSpareAmmoForKind(presence, 1),
    spareAmmoPistol: getSpareAmmoForKind(presence, 2),
    spareAmmoMp7: getSpareAmmoForKind(presence, 3),
  };
}

function applyClientSpareAmmoPayload(presence, message) {
  if (!presence || !message) {
    return;
  }

  if (Number.isFinite(message.spareAmmoAssault) && message.spareAmmoAssault >= 0) {
    setSpareAmmoForKind(presence, 0, message.spareAmmoAssault);
  }

  if (Number.isFinite(message.spareAmmoSniper) && message.spareAmmoSniper >= 0) {
    setSpareAmmoForKind(presence, 1, message.spareAmmoSniper);
  }

  if (Number.isFinite(message.spareAmmoPistol) && message.spareAmmoPistol >= 0) {
    setSpareAmmoForKind(presence, 2, message.spareAmmoPistol);
  }

  if (Number.isFinite(message.spareAmmoMp7) && message.spareAmmoMp7 >= 0) {
    setSpareAmmoForKind(presence, 3, message.spareAmmoMp7);
  }
}

function findFirstEmptyWeaponSlot(presence) {
  if (!isWeaponSlotOccupied(presence.weaponSlot0Kind)) {
    return 0;
  }
  if (!isWeaponSlotOccupied(presence.weaponSlot1Kind)) {
    return 1;
  }
  return -1;
}

function resolveDropSlotIndex(presence, requestedSlot) {
  const slot = normalizeInt64(requestedSlot, -1);
  if (slot === 0 || slot === 1) {
    return slot;
  }

  if (presence.activeWeaponSlot === 0 || presence.activeWeaponSlot === 1) {
    return presence.activeWeaponSlot;
  }

  if (isWeaponSlotOccupied(presence.weaponSlot0Kind)) {
    return 0;
  }
  if (isWeaponSlotOccupied(presence.weaponSlot1Kind)) {
    return 1;
  }
  return -1;
}

function buildWeaponLoadoutPayload(presence) {
  return {
    weaponSlot0Kind: Number.isFinite(presence.weaponSlot0Kind) ? presence.weaponSlot0Kind : WEAPON_SLOT_EMPTY,
    weaponSlot1Kind: Number.isFinite(presence.weaponSlot1Kind) ? presence.weaponSlot1Kind : WEAPON_SLOT_EMPTY,
    weaponSlot0ItemId: typeof presence.weaponSlot0ItemId === "string" ? presence.weaponSlot0ItemId : "",
    weaponSlot1ItemId: typeof presence.weaponSlot1ItemId === "string" ? presence.weaponSlot1ItemId : "",
    activeWeaponSlot: Number.isFinite(presence.activeWeaponSlot) ? presence.activeWeaponSlot : WEAPON_SLOT_EMPTY,
    bothHolstered: !!presence.isHolstered,
  };
}

function createDroppedWeaponSpawn(matchId, ticketId, position, yaw, itemId, magAmmo = -1, clientDropPosition = null) {
  const state = ensureMatchPickups(matchId);
  if (!state || !position) {
    return "";
  }

  const seq = (droppedWeaponSeqByMatchId.get(matchId) || 0) + 1;
  droppedWeaponSeqByMatchId.set(matchId, seq);
  const spawnId = `drop_${ticketId}_${seq}`;
  let x;
  let y;
  let z;
  if (clientDropPosition &&
      Number.isFinite(clientDropPosition.x) &&
      Number.isFinite(clientDropPosition.y) &&
      Number.isFinite(clientDropPosition.z)) {
    x = clientDropPosition.x;
    y = clientDropPosition.y;
    z = clientDropPosition.z;
  } else {
    const rad = (Number.isFinite(yaw) ? yaw : 0) * Math.PI / 180;
    const offset = 1.2;
    x = position.x + Math.sin(rad) * offset;
    z = position.z + Math.cos(rad) * offset;
    y = position.y;
  }
  const normalizedMagAmmo = Number.isFinite(magAmmo) ? Math.max(-1, Math.min(999, magAmmo)) : -1;
  state.spawns.set(spawnId, {
    spawnId,
    pickupKind: "weapon",
    itemId: itemId || "",
    weaponId: itemId || "",
    amount: 1,
    magAmmo: normalizedMagAmmo,
    x,
    y,
    z,
    available: true,
    respawnDelaySeconds: 0,
    respawnAtMs: 0,
  });
  state.version += 1;
  return spawnId;
}

function createDroppedItemSpawn(
  matchId,
  ticketId,
  position,
  yaw,
  pickupKind,
  itemId,
  amount = 1,
  clientDropPosition = null
) {
  const state = ensureMatchPickups(matchId);
  if (!state || !position) {
    return "";
  }

  const normalizedKind = normalizePickupKind(pickupKind, "medkit");
  const normalizedItemId = typeof itemId === "string" ? itemId.trim() : "";
  const normalizedAmount = Math.max(1, normalizeInt64(amount, 1));
  const seq = (droppedWeaponSeqByMatchId.get(matchId) || 0) + 1;
  droppedWeaponSeqByMatchId.set(matchId, seq);
  const spawnId = `drop_${ticketId}_${seq}`;
  let x;
  let y;
  let z;
  if (clientDropPosition &&
      Number.isFinite(clientDropPosition.x) &&
      Number.isFinite(clientDropPosition.y) &&
      Number.isFinite(clientDropPosition.z)) {
    x = clientDropPosition.x;
    y = clientDropPosition.y;
    z = clientDropPosition.z;
  } else {
    const rad = (Number.isFinite(yaw) ? yaw : 0) * Math.PI / 180;
    const offset = 1.2;
    x = position.x + Math.sin(rad) * offset;
    z = position.z + Math.cos(rad) * offset;
    y = position.y;
  }

  state.spawns.set(spawnId, {
    spawnId,
    pickupKind: normalizedKind,
    itemId: normalizedItemId,
    weaponId: normalizedKind === "weapon" ? normalizedItemId : "",
    amount: normalizedAmount,
    magAmmo: -1,
    x,
    y,
    z,
    available: true,
    respawnDelaySeconds: 0,
    respawnAtMs: 0,
  });
  state.version += 1;
  return spawnId;
}

function swapWeaponSlotData(presence, slotA, slotB) {
  if (!presence || slotA === slotB) {
    return;
  }

  const kindA = getWeaponSlotKind(presence, slotA);
  const kindB = getWeaponSlotKind(presence, slotB);
  const itemA = getWeaponSlotItemId(presence, slotA);
  const itemB = getWeaponSlotItemId(presence, slotB);
  const magA = getWeaponSlotMagAmmo(presence, slotA);
  const magB = getWeaponSlotMagAmmo(presence, slotB);

  setWeaponSlot(presence, slotA, itemB, kindB);
  setWeaponSlot(presence, slotB, itemA, kindA);
  setWeaponSlotMagAmmo(presence, slotA, magB);
  setWeaponSlotMagAmmo(presence, slotB, magA);

  if (presence.activeWeaponSlot === slotA) {
    presence.activeWeaponSlot = slotB;
  } else if (presence.activeWeaponSlot === slotB) {
    presence.activeWeaponSlot = slotA;
  }

  syncWeaponPresenceFlags(presence);
}

function readPoseBufferF32(buffer, offset) {
  return buffer.readFloatLE(offset);
}

function readPoseBufferI16(buffer, offset) {
  return buffer.readInt16LE(offset);
}

function readPoseBufferU16(buffer, offset) {
  return buffer.readUInt16LE(offset);
}

function readPoseBufferI32(buffer, offset) {
  return buffer.readInt32LE(offset);
}

function readPoseBufferU32(buffer, offset) {
  return buffer.readUInt32LE(offset);
}

const SKIN_SLOT_COUNT = 6;
const WEAPON_SKIN_SLOT_COUNT = 4;
const MAX_SKIN_ID_LEN = 48;

function normalizeSkinId(value) {
  if (typeof value !== "string") {
    return "";
  }
  const trimmed = value.trim();
  return trimmed.length > MAX_SKIN_ID_LEN ? trimmed.slice(0, MAX_SKIN_ID_LEN) : trimmed;
}

function normalizeSkinIds(message) {
  return {
    skinShirt: normalizeSkinId(message?.skinShirt),
    skinPants: normalizeSkinId(message?.skinPants),
    skinBoots: normalizeSkinId(message?.skinBoots),
    skinGloves: normalizeSkinId(message?.skinGloves),
    skinFace: normalizeSkinId(message?.skinFace),
    skinHair: normalizeSkinId(message?.skinHair),
    skinWeaponAssault: normalizeSkinId(message?.skinWeaponAssault),
    skinWeaponSniper: normalizeSkinId(message?.skinWeaponSniper),
    skinWeaponPistol: normalizeSkinId(message?.skinWeaponPistol),
    skinWeaponMp7: normalizeSkinId(message?.skinWeaponMp7),
  };
}

function applySkinIdsToPresence(presence, skins) {
  if (!presence || !skins) {
    return;
  }
  presence.skinShirt = skins.skinShirt || "";
  presence.skinPants = skins.skinPants || "";
  presence.skinBoots = skins.skinBoots || "";
  presence.skinGloves = skins.skinGloves || "";
  presence.skinFace = skins.skinFace || "";
  presence.skinHair = skins.skinHair || "";
  presence.skinWeaponAssault = skins.skinWeaponAssault || "";
  presence.skinWeaponSniper = skins.skinWeaponSniper || "";
  presence.skinWeaponPistol = skins.skinWeaponPistol || "";
  presence.skinWeaponMp7 = skins.skinWeaponMp7 || "";
}

function readSkinBlock(buffer, offset, slotCount = SKIN_SLOT_COUNT) {
  const ids = [];
  for (let i = 0; i < slotCount; i++) {
    const len = buffer[offset];
    offset += 1;
    if (len > MAX_SKIN_ID_LEN || offset + len > buffer.length) {
      return null;
    }
    ids.push(len > 0 ? buffer.toString("utf8", offset, offset + len) : "");
    offset += len;
  }
  return { ids, offset };
}

function writeSkinBlockChunks(chunks, presence) {
  const ids = [
    presence?.skinShirt || "",
    presence?.skinPants || "",
    presence?.skinBoots || "",
    presence?.skinGloves || "",
    presence?.skinFace || "",
    presence?.skinHair || "",
  ];
  for (let i = 0; i < ids.length; i++) {
    const bytes = Buffer.from(ids[i].slice(0, MAX_SKIN_ID_LEN), "utf8");
    chunks.push(Buffer.from([bytes.length]));
    if (bytes.length > 0) {
      chunks.push(bytes);
    }
  }
}

function writeWeaponSkinBlockChunks(chunks, presence) {
  const ids = [
    presence?.skinWeaponAssault || "",
    presence?.skinWeaponSniper || "",
    presence?.skinWeaponPistol || "",
    presence?.skinWeaponMp7 || "",
  ];
  for (let i = 0; i < ids.length; i++) {
    const bytes = Buffer.from(ids[i].slice(0, MAX_SKIN_ID_LEN), "utf8");
    chunks.push(Buffer.from([bytes.length]));
    if (bytes.length > 0) {
      chunks.push(bytes);
    }
  }
}

function decodeBinaryPose(buffer) {
  if (!Buffer.isBuffer(buffer) || buffer.length < 12) {
    return null;
  }

  if (buffer[0] !== 0x52 || buffer[1] !== 0x54 || buffer[2] !== 0x50 || buffer[3] !== 0x31) {
    return null;
  }

  const version = buffer[4];
  if (version !== 1 && version !== 2 && version !== 3) {
    return null;
  }

  let offset = 5;
  const poseSeq = readPoseBufferI32(buffer, offset);
  offset += 4;
  const modelLen = buffer[offset];
  offset += 1;
  if (offset + modelLen > buffer.length) {
    return null;
  }

  const characterModel = modelLen > 0 ? buffer.toString("utf8", offset, offset + modelLen) : "";
  offset += modelLen;

  let skinShirt = "";
  let skinPants = "";
  let skinBoots = "";
  let skinGloves = "";
  let skinFace = "";
  let skinHair = "";
  let skinWeaponAssault = "";
  let skinWeaponSniper = "";
  let skinWeaponPistol = "";
  let skinWeaponMp7 = "";
  if (version >= 2) {
    if (version >= 3) {
      const legacyBlock = readSkinBlock(buffer, offset, SKIN_SLOT_COUNT + WEAPON_SKIN_SLOT_COUNT);
      if (!legacyBlock) {
        return null;
      }
      offset = legacyBlock.offset;
      skinShirt = legacyBlock.ids[0] || "";
      skinPants = legacyBlock.ids[1] || "";
      skinBoots = legacyBlock.ids[2] || "";
      skinGloves = legacyBlock.ids[3] || "";
      skinFace = legacyBlock.ids[4] || "";
      skinHair = legacyBlock.ids[5] || "";
      skinWeaponAssault = legacyBlock.ids[6] || "";
      skinWeaponSniper = legacyBlock.ids[7] || "";
      skinWeaponPistol = legacyBlock.ids[8] || "";
      skinWeaponMp7 = legacyBlock.ids[9] || "";
    } else {
      const clothingBlock = readSkinBlock(buffer, offset, SKIN_SLOT_COUNT);
      if (!clothingBlock) {
        return null;
      }
      offset = clothingBlock.offset;
      skinShirt = clothingBlock.ids[0] || "";
      skinPants = clothingBlock.ids[1] || "";
      skinBoots = clothingBlock.ids[2] || "";
      skinGloves = clothingBlock.ids[3] || "";
      skinFace = clothingBlock.ids[4] || "";
      skinHair = clothingBlock.ids[5] || "";
    }
  }

  const minSize = offset + 121;
  if (buffer.length < minSize) {
    return null;
  }

  const x = readPoseBufferF32(buffer, offset); offset += 4;
  const y = readPoseBufferF32(buffer, offset); offset += 4;
  const z = readPoseBufferF32(buffer, offset); offset += 4;
  const yaw = readPoseBufferF32(buffer, offset); offset += 4;
  const lookPitch = readPoseBufferF32(buffer, offset); offset += 4;
  const flags = readPoseBufferU16(buffer, offset); offset += 2;
  const jumpState = buffer[offset]; offset += 1;
  const weaponKind = buffer[offset]; offset += 1;
  const weaponSlot0Kind = buffer[offset]; offset += 1;
  const weaponSlot1Kind = buffer[offset]; offset += 1;
  const activeWeaponSlot = buffer[offset]; offset += 1;
  const activeWeaponMagAmmo = readPoseBufferI16(buffer, offset); offset += 2;
  const weaponPickupSeq = readPoseBufferU32(buffer, offset); offset += 4;
  const shotSeq = readPoseBufferU32(buffer, offset); offset += 4;
  const reloadSeq = readPoseBufferU32(buffer, offset); offset += 4;
  const hitPlayerSeq = readPoseBufferU32(buffer, offset); offset += 4;
  const footstepSeq = readPoseBufferU32(buffer, offset); offset += 4;
  const deathSeq = readPoseBufferU32(buffer, offset); offset += 4;
  const animSpeed = readPoseBufferF32(buffer, offset); offset += 4;
  const animPhase = readPoseBufferF32(buffer, offset); offset += 4;
  const wallAvoidBlend = readPoseBufferF32(buffer, offset); offset += 4;
  const moveInputX = readPoseBufferF32(buffer, offset); offset += 4;
  const moveInputZ = readPoseBufferF32(buffer, offset); offset += 4;
  const deathFallDirX = readPoseBufferF32(buffer, offset); offset += 4;
  const deathFallDirY = readPoseBufferF32(buffer, offset); offset += 4;
  const deathFallDirZ = readPoseBufferF32(buffer, offset); offset += 4;
  const shotOriginX = readPoseBufferF32(buffer, offset); offset += 4;
  const shotOriginY = readPoseBufferF32(buffer, offset); offset += 4;
  const shotOriginZ = readPoseBufferF32(buffer, offset); offset += 4;
  const shotDirX = readPoseBufferF32(buffer, offset); offset += 4;
  const shotDirY = readPoseBufferF32(buffer, offset); offset += 4;
  const shotDirZ = readPoseBufferF32(buffer, offset); offset += 4;
  const shotEndX = readPoseBufferF32(buffer, offset); offset += 4;
  const shotEndY = readPoseBufferF32(buffer, offset); offset += 4;
  const shotEndZ = readPoseBufferF32(buffer, offset); offset += 4;

  if (version === 2 && offset < buffer.length) {
    const weaponBlock = readSkinBlock(buffer, offset, WEAPON_SKIN_SLOT_COUNT);
    if (weaponBlock) {
      offset = weaponBlock.offset;
      skinWeaponAssault = weaponBlock.ids[0] || "";
      skinWeaponSniper = weaponBlock.ids[1] || "";
      skinWeaponPistol = weaponBlock.ids[2] || "";
      skinWeaponMp7 = weaponBlock.ids[3] || "";
    }
  }

  return {
    type: "pose",
    poseSeq,
    characterModel,
    skinShirt,
    skinPants,
    skinBoots,
    skinGloves,
    skinFace,
    skinHair,
    skinWeaponAssault,
    skinWeaponSniper,
    skinWeaponPistol,
    skinWeaponMp7,
    position: { x, y, z },
    yaw,
    lookPitch,
    isCrouching: (flags & 1) !== 0,
    isSprinting: (flags & 2) !== 0,
    isDead: (flags & 4) !== 0,
    isHolstered: (flags & 8) !== 0,
    isGrounded: (flags & 16) !== 0,
    inputAuth: (flags & 32) !== 0,
    jumpPressed: (flags & 64) !== 0,
    shotHasEndPoint: (flags & 128) !== 0,
    isAiming: (flags & 256) !== 0,
    isSwimming: (flags & 512) !== 0,
    jumpState,
    weaponKind,
    weaponSlot0Kind,
    weaponSlot1Kind,
    activeWeaponSlot,
    activeWeaponMagAmmo,
    weaponPickupSeq,
    shotSeq,
    reloadSeq,
    hitPlayerSeq,
    footstepSeq,
    deathSeq,
    animSpeed,
    animPhase,
    wallAvoidBlend,
    moveInputX,
    moveInputZ,
    deathFallDirX,
    deathFallDirY,
    deathFallDirZ,
    shotOriginX,
    shotOriginY,
    shotOriginZ,
    shotDirX,
    shotDirY,
    shotDirZ,
    shotEndX,
    shotEndY,
    shotEndZ
  };
}

function handleWsPose(socket, message) {
  const meta = wsMetaBySocket.get(socket);
  if (!meta || !meta.ticketId) {
    return;
  }

  const ticket = ticketsById.get(meta.ticketId);
  if (!ticket || ticket.status !== "Matched") {
    return;
  }

  const position = normalizePosition(message.position);
  const yaw = normalizeNumber(message.yaw, 0);
  const lookPitch = normalizeNumber(message.lookPitch, 0);
  const characterModel = typeof message.characterModel === "string" ? message.characterModel.trim() : "";
  const skins = normalizeSkinIds(message);
  const shotSeq = Math.max(0, normalizeInt64(message.shotSeq, 0));
  const shotOriginX = normalizeNumber(message.shotOriginX, 0);
  const shotOriginY = normalizeNumber(message.shotOriginY, 0);
  const shotOriginZ = normalizeNumber(message.shotOriginZ, 0);
  const shotDirX = normalizeNumber(message.shotDirX, 0);
  const shotDirY = normalizeNumber(message.shotDirY, 0);
  const shotDirZ = normalizeNumber(message.shotDirZ, 0);
  const shotEndX = normalizeNumber(message.shotEndX, 0);
  const shotEndY = normalizeNumber(message.shotEndY, 0);
  const shotEndZ = normalizeNumber(message.shotEndZ, 0);
  const shotHasEndPoint = !!message.shotHasEndPoint;
  const reloadSeq = Math.max(0, normalizeInt64(message.reloadSeq, 0));
  const hitPlayerSeq = Math.max(0, normalizeInt64(message.hitPlayerSeq, 0));
  const footstepSeq = Math.max(0, normalizeInt64(message.footstepSeq, 0));
  const isCrouching = !!message.isCrouching;
  const isSprinting = !!message.isSprinting;
  const isSwimming = !!message.isSwimming;
  const wallAvoidBlend = Math.max(0, Math.min(1, normalizeNumber(message.wallAvoidBlend, 0)));
  const isDead = !!message.isDead;
  const deathSeq = Math.max(0, normalizeInt64(message.deathSeq, 0));
  const deathFallDirX = normalizeNumber(message.deathFallDirX, 0);
  const deathFallDirY = normalizeNumber(message.deathFallDirY, 0);
  const deathFallDirZ = normalizeNumber(message.deathFallDirZ, 0);
  const animSpeed = Math.max(0, Math.min(1.25, normalizeNumber(message.animSpeed, 0)));
  const isAiming = !!message.isAiming;
  const isHolstered = !!message.isHolstered;
  const isGrounded = !!message.isGrounded;
  const jumpState = Math.max(0, Math.min(2, normalizeInt64(message.jumpState, isGrounded ? 0 : 2)));
  const rawAnimPhase = normalizeNumber(message.animPhase, 0);
  const animPhase = ((rawAnimPhase % 1) + 1) % 1;
  const poseSeq = normalizeInt64(message.poseSeq, -1);
  const moveInputX = normalizeNumber(message.moveInputX, 0);
  const moveInputZ = normalizeNumber(message.moveInputZ, 0);
  const jumpPressed = !!message.jumpPressed;
  const inputAuth = !!message.inputAuth;
  const serverSampleTimeMs = Date.now();
  const prevPresence = ticket.presence || {};
  const weaponKind = Math.max(0, Math.min(WEAPON_KIND_MAX, normalizeInt64(message.weaponKind, prevPresence.weaponKind || 0)));
  const weaponSlot0Kind = Math.max(
    0,
    Math.min(255, normalizeInt64(message.weaponSlot0Kind, prevPresence.weaponSlot0Kind ?? WEAPON_SLOT_EMPTY))
  );
  const weaponSlot1Kind = Math.max(
    0,
    Math.min(255, normalizeInt64(message.weaponSlot1Kind, prevPresence.weaponSlot1Kind ?? WEAPON_SLOT_EMPTY))
  );
  const activeWeaponSlot = Math.max(
    0,
    Math.min(255, normalizeInt64(message.activeWeaponSlot, prevPresence.activeWeaponSlot ?? WEAPON_SLOT_EMPTY))
  );
  const activeWeaponMagAmmo = Math.max(
    -1,
    Math.min(999, normalizeInt64(message.activeWeaponMagAmmo, -1))
  );
  const clientWeaponPickupSeq = Math.max(0, normalizeInt64(message.weaponPickupSeq, 0));
  const prevWasDead = !!prevPresence.isDead;

  ticket.inputState = {
    moveInputX,
    moveInputZ,
    jumpPressed,
    inputAuth,
    yaw,
    isCrouching,
    isSprinting,
    isGrounded,
    jumpState,
    clientX: position.x,
    clientY: position.y,
    clientZ: position.z,
    lastInputMs: serverSampleTimeMs
  };

  if (!ticket.presence) {
    ticket.presence = createDefaultPresence(currentServerTick, serverSampleTimeMs);
  }

  const presence = ticket.presence;
  const prevServerPos = presence.hasPose && presence.position
    ? { x: presence.position.x, y: presence.position.y, z: presence.position.z }
    : null;
  let positionBranch = "unchanged";
  if (!presence.hasPose) {
    presence.position = { x: position.x, y: position.y, z: position.z };
    presence.hasPose = true;
    presence.velocityX = 0;
    presence.velocityY = 0;
    presence.velocityZ = 0;
    positionBranch = "first_pose";
  }

  if (!isDead && prevWasDead) {
    if (isBattleRoyaleRespawnBlocked(ticket)) {
      presence.isDead = true;
      presence.health = 0;
      positionBranch = "br_dead_no_respawn";
    } else {
      presence.position = { x: position.x, y: position.y, z: position.z };
      presence.velocityX = 0;
      presence.velocityY = 0;
      presence.velocityZ = 0;
      presence.verticalVelocity = 0;
      const maxHealth = Number.isFinite(presence.maxHealth) ? presence.maxHealth : 100;
      presence.health = maxHealth;
      positionBranch = "respawn";
    }
  } else if (isDead) {
    presence.position = { x: position.x, y: position.y, z: position.z };
    presence.velocityX = 0;
    presence.velocityY = 0;
    presence.velocityZ = 0;
    presence.verticalVelocity = 0;
    positionBranch = "dead";
  } else if (isBattleRoyaleOnPlane(ticket)) {
    presence.position = { x: position.x, y: position.y, z: position.z };
    presence.yaw = yaw;
    presence.isGrounded = isGrounded;
    presence.velocityX = 0;
    presence.velocityY = 0;
    presence.velocityZ = 0;
    positionBranch = "br_on_plane";
  } else if (isBattleRoyaleParachuting(ticket)) {
    presence.position = { x: position.x, y: position.y, z: position.z };
    presence.yaw = yaw;
    presence.isGrounded = isGrounded;
    presence.velocityX = 0;
    presence.velocityY = 0;
    presence.velocityZ = 0;
    positionBranch = "br_parachute";
  } else if (!inputAuth) {
    const clamped = clampPositionToMovement(presence, position, currentServerTick);
    presence.position = clamped;
    presence.yaw = yaw;
    positionBranch = "no_input_auth_clamp";
  } else {
    presence.yaw = yaw;
    const clamped = clampPositionToMovement(presence, position, currentServerTick);
    presence.position = {
      x: clamped.x,
      y: position.y,
      z: clamped.z
    };
    positionBranch = "input_auth_clamp_xz";
  }

  presence.lookPitch = lookPitch;
  presence.characterModel = characterModel;
  applySkinIdsToPresence(presence, skins);
  presence.shotSeq = shotSeq;
  if (shotSeq > (prevPresence.shotSeq || 0)) {
    recordShotEvent(ticket, {
      seq: shotSeq,
      shotOriginX,
      shotOriginY,
      shotOriginZ,
      shotDirX,
      shotDirY,
      shotDirZ,
      shotEndX,
      shotEndY,
      shotEndZ,
      shotHasEndPoint,
    });
  }
  presence.reloadSeq = reloadSeq;
  presence.hitPlayerSeq = hitPlayerSeq;
  presence.footstepSeq = footstepSeq;
  presence.isCrouching = isCrouching;
  presence.isSprinting = isSprinting;
  presence.isSwimming = isSwimming;
  presence.wallAvoidBlend = wallAvoidBlend;
  const lockServerDeath = shouldLockBattleRoyaleDeathState(ticket, prevWasDead);
  presence.isDead = lockServerDeath ? true : isDead;
  if (lockServerDeath) {
    presence.health = 0;
  }
  presence.deathSeq = deathSeq;
  presence.deathFallDirX = deathFallDirX;
  presence.deathFallDirY = deathFallDirY;
  presence.deathFallDirZ = deathFallDirZ;
  presence.animSpeed = animSpeed;
  presence.isAiming = isAiming;
  presence.isHolstered = isHolstered;
  const serverWeaponPickupSeq = Math.max(0, normalizeInt64(presence.weaponPickupSeq, 0));
  if (clientWeaponPickupSeq >= serverWeaponPickupSeq) {
    presence.weaponSlot0Kind = weaponSlot0Kind;
    presence.weaponSlot1Kind = weaponSlot1Kind;
    presence.activeWeaponSlot = activeWeaponSlot;
    presence.weaponKind = weaponKind;
    if (activeWeaponMagAmmo >= 0 &&
        (activeWeaponSlot === 0 || activeWeaponSlot === 1) &&
        isWeaponSlotOccupied(getWeaponSlotKind(presence, activeWeaponSlot))) {
      setWeaponSlotMagAmmo(presence, activeWeaponSlot, activeWeaponMagAmmo);
    }
  }
  syncWeaponPresenceFlags(presence);
  presence.isGrounded = isGrounded;
  presence.jumpState = jumpState;
  presence.animPhase = animPhase;
  presence.sampleTimeMs = serverSampleTimeMs;
  presence.serverSampleTimeMs = serverSampleTimeMs;
  presence.sampleTick = currentServerTick;
  presence.lastSeenMs = serverSampleTimeMs;

  if (DEBUG_REALTIME) {
    const last = lastPoseDebugByTicket.get(ticket.ticketId) || 0;
    if (serverSampleTimeMs - last >= 1000) {
      lastPoseDebugByTicket.set(ticket.ticketId, serverSampleTimeMs);
      const pos = presence.position;
      console.log(
        `[rt][pose] ticket=${ticket.ticketId} match=${ticket.matchId || "none"} ` +
        `pos=(${pos.x.toFixed(2)},${pos.y.toFixed(2)},${pos.z.toFixed(2)}) ` +
        `inputAuth=${inputAuth ? 1 : 0} holstered=${isHolstered ? 1 : 0} ` +
        `slots=${presence.weaponSlot0Kind},${presence.weaponSlot1Kind} active=${presence.activeWeaponSlot} ` +
        `hasWeapon=${presence.hasWeapon ? 1 : 0} kind=${presence.weaponKind}`
      );
    }
  }

  if (DEBUG_REALTIME && DEBUG_MOVEMENT) {
    const lastDiag = lastMoveDiagByTicket.get(ticket.ticketId) || 0;
    if (serverSampleTimeMs - lastDiag >= 1000) {
      lastMoveDiagByTicket.set(ticket.ticketId, serverSampleTimeMs);
      const prevClient = lastClientPosePosByTicket.get(ticket.ticketId);
      const clientDelta = prevClient
        ? Math.hypot(position.x - prevClient.x, position.z - prevClient.z)
        : -1;
      lastClientPosePosByTicket.set(ticket.ticketId, {
        x: position.x,
        y: position.y,
        z: position.z
      });
      const serverDelta = prevServerPos
        ? Math.hypot(
            presence.position.x - prevServerPos.x,
            presence.position.z - prevServerPos.z
          )
        : -1;
      const moveMag = Math.hypot(moveInputX, moveInputZ);
      const clientFrozen = clientDelta >= 0 && clientDelta < 0.001 && moveMag > 0.05 ? 1 : 0;
      const clientServerGap = Math.hypot(
        position.x - presence.position.x,
        position.z - presence.position.z
      );
      const serverClamped = clientServerGap > 0.05 ? 1 : 0;
      console.log(
        `[MoveDiag][server] ticket=${ticket.ticketId.slice(0, 8)} branch=${positionBranch} ` +
        `inputAuth=${inputAuth ? 1 : 0} move=(${moveInputX.toFixed(2)},${moveInputZ.toFixed(2)}) mag=${moveMag.toFixed(2)} ` +
        `client=(${position.x.toFixed(2)},${position.y.toFixed(2)},${position.z.toFixed(2)}) ` +
        `server=(${presence.position.x.toFixed(2)},${presence.position.y.toFixed(2)},${presence.position.z.toFixed(2)}) ` +
        `clientDelta=${clientDelta.toFixed(3)} serverDelta=${serverDelta.toFixed(3)} gap=${clientServerGap.toFixed(3)} ` +
        `CLIENT_FROZEN=${clientFrozen} SERVER_CLAMPED=${serverClamped} medkit=${presence.isUsingMedkit ? 1 : 0} ` +
        `jump=${jumpPressed ? 1 : 0} grounded=${isGrounded ? 1 : 0} poseSeq=${poseSeq}`
      );
    }
  }

  const poseTelemetry = ensureTicketTelemetry(ticket);
  poseTelemetry.poseReceived += 1;
  if (poseSeq >= 0) {
    if (poseTelemetry.lastPoseSeq >= 0) {
      if (poseSeq > poseTelemetry.lastPoseSeq + 1) {
        poseTelemetry.poseMissing += (poseSeq - poseTelemetry.lastPoseSeq - 1);
      } else if (poseSeq <= poseTelemetry.lastPoseSeq) {
        poseTelemetry.poseOutOfOrder += 1;
      }
    }
    poseTelemetry.lastPoseSeq = poseSeq;
  }
  touchMatchSession(ticket.matchId);
  if (positionBranch === "first_pose" && ticket.matchId) {
    broadcastMatchSnapshots(ticket.matchId);
  }
}

function normalizeShotEvent(message) {
  return {
    seq: Math.max(0, normalizeInt64(message.shotSeq, 0)),
    shotOriginX: normalizeNumber(message.shotOriginX, 0),
    shotOriginY: normalizeNumber(message.shotOriginY, 0),
    shotOriginZ: normalizeNumber(message.shotOriginZ, 0),
    shotDirX: normalizeNumber(message.shotDirX, 0),
    shotDirY: normalizeNumber(message.shotDirY, 0),
    shotDirZ: normalizeNumber(message.shotDirZ, 0),
    shotEndX: normalizeNumber(message.shotEndX, 0),
    shotEndY: normalizeNumber(message.shotEndY, 0),
    shotEndZ: normalizeNumber(message.shotEndZ, 0),
    shotHasEndPoint: !!message.shotHasEndPoint,
  };
}

function recordShotEvent(ticket, event) {
  if (!ticket || !event) {
    return false;
  }

  const seq = Math.max(0, normalizeInt64(event.seq, 0));
  if (seq <= 0) {
    return false;
  }

  if (!ticket.presence) {
    ticket.presence = createDefaultPresence(currentServerTick, Date.now());
  }

  const presence = ticket.presence;
  const prevSeq = Number.isFinite(presence.shotSeq) ? presence.shotSeq : 0;
  if (seq < prevSeq) {
    return false;
  }

  const record = {
    seq,
    shotOriginX: normalizeNumber(event.shotOriginX, 0),
    shotOriginY: normalizeNumber(event.shotOriginY, 0),
    shotOriginZ: normalizeNumber(event.shotOriginZ, 0),
    shotDirX: normalizeNumber(event.shotDirX, 0),
    shotDirY: normalizeNumber(event.shotDirY, 0),
    shotDirZ: normalizeNumber(event.shotDirZ, 0),
    shotEndX: normalizeNumber(event.shotEndX, 0),
    shotEndY: normalizeNumber(event.shotEndY, 0),
    shotEndZ: normalizeNumber(event.shotEndZ, 0),
    shotHasEndPoint: !!event.shotHasEndPoint,
  };

  let ring = Array.isArray(presence.shotRing) ? presence.shotRing.slice() : [];
  const existingIndex = ring.findIndex((item) => item && item.seq === seq);
  if (existingIndex >= 0) {
    ring[existingIndex] = record;
  } else {
    ring.push(record);
    ring.sort((a, b) => (a.seq || 0) - (b.seq || 0));
    while (ring.length > 8) {
      ring.shift();
    }
  }
  presence.shotRing = ring;

  presence.shotSeq = seq;
  presence.shotOriginX = record.shotOriginX;
  presence.shotOriginY = record.shotOriginY;
  presence.shotOriginZ = record.shotOriginZ;
  presence.shotDirX = record.shotDirX;
  presence.shotDirY = record.shotDirY;
  presence.shotDirZ = record.shotDirZ;
  presence.shotEndX = record.shotEndX;
  presence.shotEndY = record.shotEndY;
  presence.shotEndZ = record.shotEndZ;
  presence.shotHasEndPoint = record.shotHasEndPoint;
  return true;
}

function handleWsShot(socket, message) {
  const meta = wsMetaBySocket.get(socket);
  if (!meta || !meta.ticketId) {
    return;
  }

  const ticket = ticketsById.get(meta.ticketId);
  if (!ticket || ticket.status !== "Matched") {
    return;
  }

  const event = normalizeShotEvent(message);
  if (!recordShotEvent(ticket, event)) {
    return;
  }

  touchMatchSession(ticket.matchId);
  if (ticket.matchId) {
    broadcastMatchSnapshots(ticket.matchId);
  }
}

function createDefaultPresence(sampleTick, sampleTimeMs) {
  return {
    position: { x: 0, y: 0, z: 0 },
    yaw: 0,
    lookPitch: 0,
    characterModel: "",
    skinShirt: "",
    skinPants: "",
    skinBoots: "",
    skinGloves: "",
    skinFace: "",
    skinHair: "",
    skinWeaponAssault: "",
    skinWeaponSniper: "",
    skinWeaponPistol: "",
    skinWeaponMp7: "",
    shotSeq: 0,
    shotOriginX: 0,
    shotOriginY: 0,
    shotOriginZ: 0,
    shotDirX: 0,
    shotDirY: 0,
    shotDirZ: 0,
    shotEndX: 0,
    shotEndY: 0,
    shotEndZ: 0,
    shotHasEndPoint: false,
    shotRing: [],
    reloadSeq: 0,
    hitPlayerSeq: 0,
    footstepSeq: 0,
    isCrouching: false,
    isSprinting: false,
    isSwimming: false,
    wallAvoidBlend: 0,
    isDead: false,
    deathSeq: 0,
    deathFallDirX: 0,
    deathFallDirY: 0,
    deathFallDirZ: 0,
    hasPose: false,
    verticalVelocity: 0,
    velocityX: 0,
    velocityY: 0,
    velocityZ: 0,
    animSpeed: 0,
    isAiming: false,
    isHolstered: false,
    isGrounded: true,
    jumpState: 0,
    animPhase: 0,
    hasWeapon: false,
    weaponKind: 0,
    weaponSlot0Kind: WEAPON_SLOT_EMPTY,
    weaponSlot1Kind: WEAPON_SLOT_EMPTY,
    weaponSlot0ItemId: "",
    weaponSlot1ItemId: "",
    activeWeaponSlot: WEAPON_SLOT_EMPTY,
    weaponSlot0MagAmmo: -1,
    weaponSlot1MagAmmo: -1,
    spareAmmoByKind: [0, 0, 0, 0],
    weaponPickupSeq: 0,
    medkitCount: MEDKIT_STARTING_COUNT,
    grenadeCount: 0,
    isUsingMedkit: false,
    medkitUseEndsAtMs: 0,
    medkitSeq: 0,
    health: 100,
    maxHealth: 100,
    killCount: 0,
    sampleTick: sampleTick || 0,
    sampleTimeMs: sampleTimeMs || 0,
    serverSampleTimeMs: sampleTimeMs || 0,
    lastSeenMs: sampleTimeMs || 0
  };
}

function ensureMatchPickups(matchId) {
  if (!matchId) {
    return null;
  }

  if (!matchPickupsByMatchId.has(matchId)) {
    matchPickupsByMatchId.set(matchId, { spawns: new Map(), version: 0 });
  }

  return matchPickupsByMatchId.get(matchId);
}

function normalizePickupKind(value, fallback = "weapon") {
  if (typeof value !== "string") {
    return fallback;
  }

  const kind = value.trim().toLowerCase();
  if (kind === "weapon" || kind === "ammo" || kind === "grenade" || kind === "medkit") {
    return kind;
  }

  return fallback;
}

function resolvePickupItemId(entry) {
  if (!entry) {
    return "";
  }

  if (typeof entry.itemId === "string" && entry.itemId.trim()) {
    return entry.itemId.trim();
  }

  if (typeof entry.weaponId === "string" && entry.weaponId.trim()) {
    return entry.weaponId.trim();
  }

  return "";
}

function handleWsRegisterPickups(socket, message) {
  const meta = wsMetaBySocket.get(socket);
  if (!meta || !meta.ticketId) {
    return;
  }

  const ticket = ticketsById.get(meta.ticketId);
  if (!ticket || ticket.status !== "Matched" || !ticket.matchId) {
    return;
  }

  const entries = Array.isArray(message.spawns) ? message.spawns : [];
  const state = ensureMatchPickups(ticket.matchId);
  if (!state) {
    return;
  }

  let added = 0;
  for (let i = 0; i < entries.length; i++) {
    const entry = entries[i];
    if (!entry || typeof entry.spawnId !== "string") {
      continue;
    }

    const spawnId = entry.spawnId.trim();
    if (!spawnId) {
      continue;
    }

    const itemId = resolvePickupItemId(entry);
    const pickupKind = normalizePickupKind(entry.pickupKind, "weapon");
    const existingSpawn = state.spawns.get(spawnId);
    if (existingSpawn) {
      continue;
    }

    state.spawns.set(spawnId, {
      spawnId,
      pickupKind,
      itemId,
      weaponId: itemId,
      amount: Math.max(1, normalizeInt64(entry.amount, 1)),
      magAmmo: pickupKind === "weapon"
        ? Math.max(0, Math.min(999, normalizeInt64(entry.magAmmo, 0)))
        : -1,
      x: normalizeNumber(entry.x, 0),
      y: normalizeNumber(entry.y, 0),
      z: normalizeNumber(entry.z, 0),
      available: true,
      respawnDelaySeconds: Math.max(0, normalizeNumber(entry.respawnDelaySeconds, 0)),
      respawnAtMs: 0
    });
    added += 1;
  }

  if (added > 0) {
    state.version += 1;
    broadcastPickupState(ticket.matchId);
  } else {
    sendPickupStateToSocket(socket, ticket.matchId);
  }
  touchMatchSession(ticket.matchId);
}

function handleWsPickup(socket, message) {
  const meta = wsMetaBySocket.get(socket);
  if (!meta || !meta.ticketId) {
    return;
  }

  const ticket = ticketsById.get(meta.ticketId);
  if (!ticket || ticket.status !== "Matched" || !ticket.matchId) {
    return;
  }

  if (!isBattleRoyaleCombatAllowed(ticket)) {
    sendPickupResultToSocket(socket, false, "match_not_started");
    return;
  }

  const spawnId = typeof message.spawnId === "string" ? message.spawnId.trim() : "";
  if (!spawnId) {
    sendPickupResultToSocket(socket, false, "invalid_spawn");
    return;
  }

  if (!ticket.presence || !ticket.presence.hasPose || !ticket.presence.position) {
    sendPickupResultToSocket(socket, false, "no_pose");
    return;
  }

  const presence = ticket.presence;
  if (presence.isDead) {
    sendPickupResultToSocket(socket, false, "dead");
    return;
  }

  const state = ensureMatchPickups(ticket.matchId);
  const spawn = state && state.spawns.get(spawnId);
  if (!spawn || !spawn.available) {
    sendPickupResultToSocket(socket, false, "unavailable");
    return;
  }

  const pickupKind = normalizePickupKind(spawn.pickupKind, "weapon");

  if (pickupKind === "medkit" && (presence.medkitCount || 0) >= MEDKIT_MAX_COUNT) {
    sendPickupResultToSocket(socket, false, "medkit_full");
    return;
  }

  if (pickupKind === "grenade" && (presence.grenadeCount || 0) >= GRENADE_MAX_COUNT) {
    sendPickupResultToSocket(socket, false, "inventory_full");
    return;
  }

  const pos = presence.position;
  const dx = pos.x - spawn.x;
  const dz = pos.z - spawn.z;
  const distSq = (dx * dx) + (dz * dz);
  if (distSq > PICKUP_MAX_DISTANCE * PICKUP_MAX_DISTANCE) {
    sendPickupResultToSocket(socket, false, "too_far");
    return;
  }

  const itemId = spawn.itemId || spawn.weaponId || "";
  const amount = Math.max(1, normalizeInt64(spawn.amount, 1));

  let weaponPickupSeq = Math.max(0, normalizeInt64(presence.weaponPickupSeq, 0));
  let droppedSpawnId = "";
  let pickedMagAmmo = -1;
  let pickedReserveAmmo = -1;
  if (pickupKind === "weapon") {
    const requestedSlot = normalizeInt64(message && message.targetWeaponSlot, -1);
    let targetSlot = -1;

    if (requestedSlot === 0 || requestedSlot === 1) {
      targetSlot = requestedSlot;
    } else {
      targetSlot = findFirstEmptyWeaponSlot(presence);
      if (targetSlot < 0) {
        targetSlot = resolveDropSlotIndex(presence, -1);
      }
    }

    if (targetSlot < 0) {
      sendPickupResultToSocket(socket, false, "inventory_full");
      return;
    }

    const dropItemId = getWeaponSlotItemId(presence, targetSlot);
    if (targetSlot >= 0 && dropItemId && isWeaponSlotOccupied(getWeaponSlotKind(presence, targetSlot))) {
      const swapMag = Math.max(0, getWeaponSlotMagAmmo(presence, targetSlot));
      droppedSpawnId = createDroppedWeaponSpawn(
        ticket.matchId,
        ticket.ticketId,
        pos,
        presence.yaw || 0,
        dropItemId,
        swapMag
      );
      if (droppedSpawnId) {
        const dropSpawn = state.spawns.get(droppedSpawnId);
        broadcastPickupEvent(ticket.matchId, {
          type: "pickup_event",
          spawnId: droppedSpawnId,
          ticketId: ticket.ticketId,
          pickupKind: "weapon",
          itemId: dropItemId,
          weaponId: dropItemId,
          amount: 1,
          available: true,
          x: dropSpawn ? dropSpawn.x : pos.x,
          y: dropSpawn ? dropSpawn.y : pos.y,
          z: dropSpawn ? dropSpawn.z : pos.z,
        });
      }
      clearWeaponSlot(presence, targetSlot);
    }

    if (targetSlot < 0) {
      sendPickupResultToSocket(socket, false, "inventory_full");
      return;
    }

    const kind = resolveWeaponKindFromItemId(itemId);
    setWeaponSlot(presence, targetSlot, itemId, kind);
    presence.activeWeaponSlot = targetSlot;
    presence.weaponKind = kind;
    presence.isHolstered = false;
    pickedMagAmmo = Number.isFinite(spawn.magAmmo) ? Math.max(0, Math.min(999, spawn.magAmmo)) : 0;
    pickedReserveAmmo = getSpareAmmo(presence);
    setWeaponSlotMagAmmo(presence, targetSlot, pickedMagAmmo);
    weaponPickupSeq += 1;
    presence.weaponPickupSeq = weaponPickupSeq;
    syncWeaponPresenceFlags(presence);
  } else if (pickupKind === "ammo") {
    const ammoKind = resolveAmmoKindFromItemId(itemId);
    if (ammoKind < 0) {
      sendPickupResultToSocket(socket, false, "unknown_ammo");
      return;
    }

    const newReserve = Math.min(999, getSpareAmmoForKind(presence, ammoKind) + amount);
    setSpareAmmoForKind(presence, ammoKind, newReserve);
    pickedReserveAmmo = getSpareAmmo(presence);

    let activeSlot = presence.activeWeaponSlot;
    if (activeSlot !== 0 && activeSlot !== 1) {
      if (isWeaponSlotOccupied(presence.weaponSlot0Kind)) {
        activeSlot = 0;
      } else if (isWeaponSlotOccupied(presence.weaponSlot1Kind)) {
        activeSlot = 1;
      } else {
        activeSlot = -1;
      }
    }

    if (activeSlot === 0 || activeSlot === 1) {
      const activeKind = activeSlot === 0 ? presence.weaponSlot0Kind : presence.weaponSlot1Kind;
      if (isWeaponSlotOccupied(activeKind)) {
        presence.activeWeaponSlot = activeSlot;
        pickedMagAmmo = Math.max(0, getWeaponSlotMagAmmo(presence, activeSlot));
      } else {
        pickedMagAmmo = -1;
      }
    } else {
      pickedMagAmmo = -1;
    }

    weaponPickupSeq = Math.max(0, normalizeInt64(presence.weaponPickupSeq, 0));
    syncWeaponPresenceFlags(presence);
  } else if (pickupKind === "medkit") {
    presence.medkitCount = Math.min(
      MEDKIT_MAX_COUNT,
      Math.max(0, normalizeInt64(presence.medkitCount, 0)) + amount
    );
  } else if (pickupKind === "grenade") {
    presence.grenadeCount = Math.min(
      GRENADE_MAX_COUNT,
      Math.max(0, normalizeInt64(presence.grenadeCount, 0)) + amount
    );
  }

  spawn.available = false;
  spawn.respawnAtMs = spawn.respawnDelaySeconds > 0.01
    ? Date.now() + (spawn.respawnDelaySeconds * 1000)
    : 0;

  sendPickupResultToSocket(socket, true, "ok", {
    spawnId,
    ticketId: ticket.ticketId,
    weaponPickupSeq,
    pickupKind,
    itemId,
    weaponId: itemId,
    amount,
    medkitCount: Math.max(0, normalizeInt64(presence.medkitCount, 0)),
    grenadeCount: Math.max(0, normalizeInt64(presence.grenadeCount, 0)),
    droppedSpawnId,
    magAmmo: pickedMagAmmo,
    reserveAmmo: pickedReserveAmmo,
    ...buildSpareAmmoPayload(presence),
    ...buildWeaponLoadoutPayload(presence),
  });

  broadcastPickupEvent(ticket.matchId, {
    type: "pickup_event",
    spawnId,
    ticketId: ticket.ticketId,
    weaponPickupSeq,
    pickupKind,
    itemId,
    weaponId: itemId,
    amount,
    available: false
  });

  touchMatchSession(ticket.matchId);
  broadcastMatchSnapshots(ticket.matchId);
}

function sendPickupResultToSocket(socket, success, reason, details) {
  if (!socket || socket.readyState !== WebSocket.OPEN) {
    return;
  }

  try {
    socket.send(JSON.stringify({
      type: "pickup_result",
      success: !!success,
      reason: typeof reason === "string" ? reason : "",
      spawnId: details && typeof details.spawnId === "string" ? details.spawnId : "",
      ticketId: details && typeof details.ticketId === "string" ? details.ticketId : "",
      weaponPickupSeq: details && Number.isFinite(details.weaponPickupSeq) ? details.weaponPickupSeq : 0,
      pickupKind: details && typeof details.pickupKind === "string" ? details.pickupKind : "",
      itemId: details && typeof details.itemId === "string" ? details.itemId : "",
      weaponId: details && typeof details.weaponId === "string" ? details.weaponId : "",
      amount: details && Number.isFinite(details.amount) ? details.amount : 0,
      medkitCount: details && Number.isFinite(details.medkitCount) ? details.medkitCount : 0,
      grenadeCount: details && Number.isFinite(details.grenadeCount) ? details.grenadeCount : 0,
      droppedSpawnId: details && typeof details.droppedSpawnId === "string" ? details.droppedSpawnId : "",
      weaponSlot0Kind: details && Number.isFinite(details.weaponSlot0Kind) ? details.weaponSlot0Kind : WEAPON_SLOT_EMPTY,
      weaponSlot1Kind: details && Number.isFinite(details.weaponSlot1Kind) ? details.weaponSlot1Kind : WEAPON_SLOT_EMPTY,
      weaponSlot0ItemId: details && typeof details.weaponSlot0ItemId === "string" ? details.weaponSlot0ItemId : "",
      weaponSlot1ItemId: details && typeof details.weaponSlot1ItemId === "string" ? details.weaponSlot1ItemId : "",
      activeWeaponSlot: details && Number.isFinite(details.activeWeaponSlot) ? details.activeWeaponSlot : WEAPON_SLOT_EMPTY,
      bothHolstered: !!(details && details.bothHolstered),
      magAmmo: details && Number.isFinite(details.magAmmo) ? details.magAmmo : -1,
      reserveAmmo: details && Number.isFinite(details.reserveAmmo) ? details.reserveAmmo : -1,
      spareAmmoAssault: details && Number.isFinite(details.spareAmmoAssault) ? details.spareAmmoAssault : -1,
      spareAmmoSniper: details && Number.isFinite(details.spareAmmoSniper) ? details.spareAmmoSniper : -1,
      spareAmmoPistol: details && Number.isFinite(details.spareAmmoPistol) ? details.spareAmmoPistol : -1,
      spareAmmoMp7: details && Number.isFinite(details.spareAmmoMp7) ? details.spareAmmoMp7 : -1,
    }));
  } catch {
    // ignored
  }
}

function sendWeaponDropResultToSocket(socket, success, reason, details) {
  if (!socket || socket.readyState !== WebSocket.OPEN) {
    return;
  }

  try {
    socket.send(JSON.stringify({
      type: "weapon_drop_result",
      success: !!success,
      reason: typeof reason === "string" ? reason : "",
      ticketId: details && typeof details.ticketId === "string" ? details.ticketId : "",
      slotIndex: details && Number.isFinite(details.slotIndex) ? details.slotIndex : -1,
      droppedSpawnId: details && typeof details.droppedSpawnId === "string" ? details.droppedSpawnId : "",
      itemId: details && typeof details.itemId === "string" ? details.itemId : "",
      x: details && Number.isFinite(details.x) ? details.x : 0,
      y: details && Number.isFinite(details.y) ? details.y : 0,
      z: details && Number.isFinite(details.z) ? details.z : 0,
      weaponSlot0Kind: details && Number.isFinite(details.weaponSlot0Kind) ? details.weaponSlot0Kind : WEAPON_SLOT_EMPTY,
      weaponSlot1Kind: details && Number.isFinite(details.weaponSlot1Kind) ? details.weaponSlot1Kind : WEAPON_SLOT_EMPTY,
      weaponSlot0ItemId: details && typeof details.weaponSlot0ItemId === "string" ? details.weaponSlot0ItemId : "",
      weaponSlot1ItemId: details && typeof details.weaponSlot1ItemId === "string" ? details.weaponSlot1ItemId : "",
      activeWeaponSlot: details && Number.isFinite(details.activeWeaponSlot) ? details.activeWeaponSlot : WEAPON_SLOT_EMPTY,
      bothHolstered: !!(details && details.bothHolstered),
      weaponPickupSeq: details && Number.isFinite(details.weaponPickupSeq) ? details.weaponPickupSeq : 0,
      magAmmo: details && Number.isFinite(details.magAmmo) ? details.magAmmo : -1,
      reserveAmmo: details && Number.isFinite(details.reserveAmmo) ? details.reserveAmmo : -1,
      spareAmmoAssault: details && Number.isFinite(details.spareAmmoAssault) ? details.spareAmmoAssault : -1,
      spareAmmoSniper: details && Number.isFinite(details.spareAmmoSniper) ? details.spareAmmoSniper : -1,
      spareAmmoPistol: details && Number.isFinite(details.spareAmmoPistol) ? details.spareAmmoPistol : -1,
      spareAmmoMp7: details && Number.isFinite(details.spareAmmoMp7) ? details.spareAmmoMp7 : -1,
    }));
  } catch {
    // ignored
  }
}

function handleWsAmmoState(socket, message) {
  const meta = wsMetaBySocket.get(socket);
  if (!meta || !meta.ticketId) {
    return;
  }

  const ticket = ticketsById.get(meta.ticketId);
  if (!ticket || ticket.status !== "Matched" || !ticket.presence) {
    return;
  }

  applyClientSpareAmmoPayload(ticket.presence, message);

  const slot0MagAmmo = Number.isFinite(message.slot0MagAmmo)
    ? Math.max(-1, Math.min(999, normalizeInt64(message.slot0MagAmmo, -1)))
    : -1;
  const slot1MagAmmo = Number.isFinite(message.slot1MagAmmo)
    ? Math.max(-1, Math.min(999, normalizeInt64(message.slot1MagAmmo, -1)))
    : -1;

  if (slot0MagAmmo >= 0 && isWeaponSlotOccupied(ticket.presence.weaponSlot0Kind)) {
    setWeaponSlotMagAmmo(ticket.presence, 0, slot0MagAmmo);
  }

  if (slot1MagAmmo >= 0 && isWeaponSlotOccupied(ticket.presence.weaponSlot1Kind)) {
    setWeaponSlotMagAmmo(ticket.presence, 1, slot1MagAmmo);
  }

  touchMatchSession(ticket.matchId);
}

function handleWsWeaponDrop(socket, message) {
  const meta = wsMetaBySocket.get(socket);
  if (!meta || !meta.ticketId) {
    return;
  }

  const ticket = ticketsById.get(meta.ticketId);
  if (!ticket || ticket.status !== "Matched" || !ticket.matchId) {
    return;
  }

  if (!ticket.presence || !ticket.presence.hasPose || !ticket.presence.position) {
    sendWeaponDropResultToSocket(socket, false, "no_pose");
    return;
  }

  const presence = ticket.presence;
  if (presence.isDead) {
    sendWeaponDropResultToSocket(socket, false, "dead");
    return;
  }

  applyClientSpareAmmoPayload(presence, message);

  const slotIndex = resolveDropSlotIndex(presence, message && message.slotIndex);
  if (slotIndex < 0 || !isWeaponSlotOccupied(getWeaponSlotKind(presence, slotIndex))) {
    sendWeaponDropResultToSocket(socket, false, "empty_slot");
    return;
  }

  const itemId = getWeaponSlotItemId(presence, slotIndex);
  const slotMagAmmo = getWeaponSlotMagAmmo(presence, slotIndex);
  const droppedMag = Math.max(
    0,
    Math.min(999, normalizeInt64(message && message.magAmmo, slotMagAmmo))
  );
  const hasClientDrop = !!(message && message.hasDropPosition);
  const clientDropPosition = hasClientDrop
    ? {
        x: normalizeNumber(message.x, NaN),
        y: normalizeNumber(message.y, NaN),
        z: normalizeNumber(message.z, NaN)
      }
    : null;
  const useClientDrop = clientDropPosition &&
    Number.isFinite(clientDropPosition.x) &&
    Number.isFinite(clientDropPosition.y) &&
    Number.isFinite(clientDropPosition.z);
  const droppedSpawnId = createDroppedWeaponSpawn(
    ticket.matchId,
    ticket.ticketId,
    presence.position,
    presence.yaw || 0,
    itemId,
    droppedMag,
    useClientDrop ? clientDropPosition : null
  );
  if (!droppedSpawnId) {
    sendWeaponDropResultToSocket(socket, false, "spawn_failed");
    return;
  }

  clearWeaponSlot(presence, slotIndex);
  if (presence.activeWeaponSlot === slotIndex) {
    presence.activeWeaponSlot = WEAPON_SLOT_EMPTY;
    presence.isHolstered = true;
    if (isWeaponSlotOccupied(presence.weaponSlot0Kind)) {
      presence.weaponKind = presence.weaponSlot0Kind;
    } else if (isWeaponSlotOccupied(presence.weaponSlot1Kind)) {
      presence.weaponKind = presence.weaponSlot1Kind;
    } else {
      presence.weaponKind = 0;
    }
  }

  let weaponPickupSeq = Math.max(0, normalizeInt64(presence.weaponPickupSeq, 0));
  weaponPickupSeq += 1;
  presence.weaponPickupSeq = weaponPickupSeq;
  syncWeaponPresenceFlags(presence);

  const state = ensureMatchPickups(ticket.matchId);
  const dropSpawn = state && state.spawns.get(droppedSpawnId);
  sendWeaponDropResultToSocket(socket, true, "ok", {
    ticketId: ticket.ticketId,
    slotIndex,
    droppedSpawnId,
    itemId,
    magAmmo: dropSpawn && Number.isFinite(dropSpawn.magAmmo) ? Math.max(0, dropSpawn.magAmmo) : 0,
    reserveAmmo: getSpareAmmo(presence),
    ...buildSpareAmmoPayload(presence),
    weaponPickupSeq,
    x: dropSpawn ? dropSpawn.x : presence.position.x,
    y: dropSpawn ? dropSpawn.y : presence.position.y,
    z: dropSpawn ? dropSpawn.z : presence.position.z,
    ...buildWeaponLoadoutPayload(presence),
  });

  broadcastPickupEvent(ticket.matchId, {
    type: "pickup_event",
    spawnId: droppedSpawnId,
    ticketId: ticket.ticketId,
    pickupKind: "weapon",
    itemId,
    weaponId: itemId,
    amount: 1,
    available: true,
    x: dropSpawn ? dropSpawn.x : presence.position.x,
    y: dropSpawn ? dropSpawn.y : presence.position.y,
    z: dropSpawn ? dropSpawn.z : presence.position.z,
  });

  touchMatchSession(ticket.matchId);
  broadcastMatchSnapshots(ticket.matchId);
}

function sendWeaponSwapResultToSocket(socket, success, reason, details) {
  if (!socket || socket.readyState !== WebSocket.OPEN) {
    return;
  }

  try {
    socket.send(JSON.stringify({
      type: "weapon_swap_result",
      success: !!success,
      reason: typeof reason === "string" ? reason : "",
      ticketId: details && typeof details.ticketId === "string" ? details.ticketId : "",
      slotA: details && Number.isFinite(details.slotA) ? details.slotA : -1,
      slotB: details && Number.isFinite(details.slotB) ? details.slotB : -1,
      weaponPickupSeq: details && Number.isFinite(details.weaponPickupSeq) ? details.weaponPickupSeq : 0,
      weaponSlot0Kind: details && Number.isFinite(details.weaponSlot0Kind) ? details.weaponSlot0Kind : WEAPON_SLOT_EMPTY,
      weaponSlot1Kind: details && Number.isFinite(details.weaponSlot1Kind) ? details.weaponSlot1Kind : WEAPON_SLOT_EMPTY,
      weaponSlot0ItemId: details && typeof details.weaponSlot0ItemId === "string" ? details.weaponSlot0ItemId : "",
      weaponSlot1ItemId: details && typeof details.weaponSlot1ItemId === "string" ? details.weaponSlot1ItemId : "",
      activeWeaponSlot: details && Number.isFinite(details.activeWeaponSlot) ? details.activeWeaponSlot : WEAPON_SLOT_EMPTY,
      bothHolstered: !!(details && details.bothHolstered),
      spareAmmoAssault: details && Number.isFinite(details.spareAmmoAssault) ? details.spareAmmoAssault : -1,
      spareAmmoSniper: details && Number.isFinite(details.spareAmmoSniper) ? details.spareAmmoSniper : -1,
      spareAmmoPistol: details && Number.isFinite(details.spareAmmoPistol) ? details.spareAmmoPistol : -1,
      spareAmmoMp7: details && Number.isFinite(details.spareAmmoMp7) ? details.spareAmmoMp7 : -1,
    }));
  } catch {
    // ignored
  }
}

function handleWsWeaponSwap(socket, message) {
  const meta = wsMetaBySocket.get(socket);
  if (!meta || !meta.ticketId) {
    return;
  }

  const ticket = ticketsById.get(meta.ticketId);
  if (!ticket || ticket.status !== "Matched" || !ticket.matchId) {
    return;
  }

  if (!ticket.presence || !ticket.presence.hasPose) {
    sendWeaponSwapResultToSocket(socket, false, "no_pose");
    return;
  }

  const presence = ticket.presence;
  if (presence.isDead) {
    sendWeaponSwapResultToSocket(socket, false, "dead");
    return;
  }

  const slotA = normalizeInt64(message && message.slotA, -1);
  const slotB = normalizeInt64(message && message.slotB, -1);
  if ((slotA !== 0 && slotA !== 1) || (slotB !== 0 && slotB !== 1) || slotA === slotB) {
    sendWeaponSwapResultToSocket(socket, false, "invalid_slot");
    return;
  }

  if (!isWeaponSlotOccupied(getWeaponSlotKind(presence, slotA)) &&
      !isWeaponSlotOccupied(getWeaponSlotKind(presence, slotB))) {
    sendWeaponSwapResultToSocket(socket, false, "empty_slot");
    return;
  }

  swapWeaponSlotData(presence, slotA, slotB);
  let weaponPickupSeq = Math.max(0, normalizeInt64(presence.weaponPickupSeq, 0));
  weaponPickupSeq += 1;
  presence.weaponPickupSeq = weaponPickupSeq;

  sendWeaponSwapResultToSocket(socket, true, "ok", {
    ticketId: ticket.ticketId,
    slotA,
    slotB,
    weaponPickupSeq,
    ...buildSpareAmmoPayload(presence),
    ...buildWeaponLoadoutPayload(presence),
  });

  touchMatchSession(ticket.matchId);
  broadcastMatchSnapshots(ticket.matchId);
}

function sendInventoryItemDropResultToSocket(socket, success, reason, details) {
  if (!socket || socket.readyState !== WebSocket.OPEN) {
    return;
  }

  try {
    socket.send(JSON.stringify({
      type: "inventory_item_drop_result",
      success: !!success,
      reason: typeof reason === "string" ? reason : "",
      ticketId: details && typeof details.ticketId === "string" ? details.ticketId : "",
      itemId: details && typeof details.itemId === "string" ? details.itemId : "",
      amount: details && Number.isFinite(details.amount) ? details.amount : 0,
      medkitCount: details && Number.isFinite(details.medkitCount) ? details.medkitCount : -1,
      grenadeCount: details && Number.isFinite(details.grenadeCount) ? details.grenadeCount : -1,
      spareAmmoAssault: details && Number.isFinite(details.spareAmmoAssault) ? details.spareAmmoAssault : -1,
      spareAmmoSniper: details && Number.isFinite(details.spareAmmoSniper) ? details.spareAmmoSniper : -1,
      spareAmmoPistol: details && Number.isFinite(details.spareAmmoPistol) ? details.spareAmmoPistol : -1,
      spareAmmoMp7: details && Number.isFinite(details.spareAmmoMp7) ? details.spareAmmoMp7 : -1,
      droppedSpawnId: details && typeof details.droppedSpawnId === "string" ? details.droppedSpawnId : "",
      x: details && Number.isFinite(details.x) ? details.x : 0,
      y: details && Number.isFinite(details.y) ? details.y : 0,
      z: details && Number.isFinite(details.z) ? details.z : 0,
    }));
  } catch {
    // ignored
  }
}

function handleWsInventoryItemDrop(socket, message) {
  const meta = wsMetaBySocket.get(socket);
  if (!meta || !meta.ticketId) {
    return;
  }

  const ticket = ticketsById.get(meta.ticketId);
  if (!ticket || ticket.status !== "Matched" || !ticket.matchId) {
    return;
  }

  if (!ticket.presence || !ticket.presence.hasPose || !ticket.presence.position) {
    sendInventoryItemDropResultToSocket(socket, false, "no_pose");
    return;
  }

  const presence = ticket.presence;
  if (presence.isDead) {
    sendInventoryItemDropResultToSocket(socket, false, "dead");
    return;
  }

  const itemId = message && typeof message.itemId === "string" ? message.itemId.trim().toLowerCase() : "";
  const amount = Math.max(1, normalizeInt64(message && message.amount, 1));
  if (!itemId) {
    sendInventoryItemDropResultToSocket(socket, false, "invalid_item");
    return;
  }

  let pickupKind = "";
  if (itemId === "medkit") {
    pickupKind = "medkit";
    const medkitCountNow = Math.max(0, normalizeInt64(presence.medkitCount, 0));
    if (medkitCountNow < amount) {
      sendInventoryItemDropResultToSocket(socket, false, "not_enough");
      return;
    }
    presence.medkitCount = medkitCountNow - amount;
  } else if (itemId === "grenade") {
    pickupKind = "grenade";
    const grenadeCountNow = Math.max(0, normalizeInt64(presence.grenadeCount, 0));
    if (grenadeCountNow < amount) {
      sendInventoryItemDropResultToSocket(socket, false, "not_enough");
      return;
    }
    presence.grenadeCount = grenadeCountNow - amount;
  } else {
    const ammoKind = resolveAmmoKindFromItemId(itemId);
    if (ammoKind < 0) {
      sendInventoryItemDropResultToSocket(socket, false, "invalid_item");
      return;
    }

    pickupKind = "ammo";
    const spareNow = getSpareAmmoForKind(presence, ammoKind);
    if (spareNow < amount) {
      sendInventoryItemDropResultToSocket(socket, false, "not_enough");
      return;
    }

    setSpareAmmoForKind(presence, ammoKind, spareNow - amount);
  }

  const hasClientDrop = !!(message && message.hasDropPosition);
  const clientDropPosition = hasClientDrop
    ? {
        x: normalizeNumber(message.x, NaN),
        y: normalizeNumber(message.y, NaN),
        z: normalizeNumber(message.z, NaN)
      }
    : null;
  const useClientDrop = clientDropPosition &&
    Number.isFinite(clientDropPosition.x) &&
    Number.isFinite(clientDropPosition.y) &&
    Number.isFinite(clientDropPosition.z);
  const droppedSpawnId = createDroppedItemSpawn(
    ticket.matchId,
    ticket.ticketId,
    presence.position,
    presence.yaw || 0,
    pickupKind,
    itemId,
    amount,
    useClientDrop ? clientDropPosition : null
  );
  if (!droppedSpawnId) {
    sendInventoryItemDropResultToSocket(socket, false, "spawn_failed");
    return;
  }

  const state = ensureMatchPickups(ticket.matchId);
  const dropSpawn = state && state.spawns.get(droppedSpawnId);
  sendInventoryItemDropResultToSocket(socket, true, "ok", {
    ticketId: ticket.ticketId,
    itemId,
    amount,
    medkitCount: Math.max(0, normalizeInt64(presence.medkitCount, 0)),
    grenadeCount: Math.max(0, normalizeInt64(presence.grenadeCount, 0)),
    ...buildSpareAmmoPayload(presence),
    droppedSpawnId,
    x: dropSpawn ? dropSpawn.x : presence.position.x,
    y: dropSpawn ? dropSpawn.y : presence.position.y,
    z: dropSpawn ? dropSpawn.z : presence.position.z,
  });

  broadcastPickupEvent(ticket.matchId, {
    type: "pickup_event",
    spawnId: droppedSpawnId,
    ticketId: ticket.ticketId,
    pickupKind,
    itemId,
    weaponId: itemId,
    amount,
    available: true,
    x: dropSpawn ? dropSpawn.x : presence.position.x,
    y: dropSpawn ? dropSpawn.y : presence.position.y,
    z: dropSpawn ? dropSpawn.z : presence.position.z,
  });

  touchMatchSession(ticket.matchId);
  broadcastMatchSnapshots(ticket.matchId);
}

function getMedkitRemainingSeconds(presence) {
  if (!presence || !presence.isUsingMedkit || !presence.medkitUseEndsAtMs) {
    return 0;
  }

  return Math.max(0, (presence.medkitUseEndsAtMs - Date.now()) / 1000);
}

function sendMedkitResultToSocket(socket, success, reason, details) {
  if (!socket || socket.readyState !== WebSocket.OPEN) {
    return;
  }

  try {
    socket.send(JSON.stringify({
      type: "medkit_result",
      success: !!success,
      reason: typeof reason === "string" ? reason : "",
      ticketId: details && typeof details.ticketId === "string" ? details.ticketId : "",
      medkitSeq: details && Number.isFinite(details.medkitSeq) ? details.medkitSeq : 0,
      durationSeconds: details && Number.isFinite(details.durationSeconds) ? details.durationSeconds : MEDKIT_USE_DURATION_SECONDS,
      medkitCount: details && Number.isFinite(details.medkitCount) ? details.medkitCount : 0
    }));
  } catch {
    // ignored
  }
}

function sendHealToSocket(socket, amount, medkitSeq) {
  if (!socket || socket.readyState !== WebSocket.OPEN) {
    return;
  }

  try {
    socket.send(JSON.stringify({
      type: "heal",
      amount: Math.max(0, amount),
      medkitSeq: Math.max(0, normalizeInt64(medkitSeq, 0))
    }));
  } catch {
    // ignored
  }
}

function handleWsMedkitUse(socket) {
  const meta = wsMetaBySocket.get(socket);
  if (!meta || !meta.ticketId) {
    return;
  }

  const ticket = ticketsById.get(meta.ticketId);
  if (!ticket || ticket.status !== "Matched") {
    return;
  }

  if (!ticket.presence) {
    ticket.presence = createDefaultPresence(currentServerTick, Date.now());
  }

  const presence = ticket.presence;
  const medkitCountNow = Math.max(0, normalizeInt64(presence.medkitCount, 0));
  if (!presence.hasPose || presence.isDead) {
    sendMedkitResultToSocket(socket, false, "dead", { medkitCount: medkitCountNow });
    return;
  }

  if (presence.isUsingMedkit) {
    sendMedkitResultToSocket(socket, false, "already_using", { medkitCount: medkitCountNow });
    return;
  }

  if (medkitCountNow <= 0) {
    sendMedkitResultToSocket(socket, false, "no_medkit", { medkitCount: medkitCountNow });
    return;
  }

  const currentHealth = Number.isFinite(presence.health) ? presence.health : 100;
  const maxHealth = Number.isFinite(presence.maxHealth) ? presence.maxHealth : 100;
  if (currentHealth >= maxHealth - 0.001) {
    sendMedkitResultToSocket(socket, false, "full_health", { medkitCount: medkitCountNow });
    return;
  }

  presence.medkitCount = medkitCountNow - 1;
  presence.isUsingMedkit = true;
  if (presence.hasWeapon) {
    presence.isHolstered = true;
  }
  presence.medkitSeq = Math.max(0, normalizeInt64(presence.medkitSeq, 0)) + 1;
  presence.medkitUseEndsAtMs = Date.now() + (MEDKIT_USE_DURATION_SECONDS * 1000);

  sendMedkitResultToSocket(socket, true, "ok", {
    ticketId: ticket.ticketId,
    medkitSeq: presence.medkitSeq,
    durationSeconds: MEDKIT_USE_DURATION_SECONDS,
    medkitCount: presence.medkitCount
  });

  touchMatchSession(ticket.matchId);
  broadcastMatchSnapshots(ticket.matchId);
}

function handleWsMedkitCancel(socket) {
  const meta = wsMetaBySocket.get(socket);
  if (!meta || !meta.ticketId) {
    return;
  }

  const ticket = ticketsById.get(meta.ticketId);
  if (!ticket || ticket.status !== "Matched" || !ticket.presence) {
    return;
  }

  const presence = ticket.presence;
  if (!presence.isUsingMedkit) {
    sendMedkitResultToSocket(socket, false, "not_using", {
      medkitCount: Math.max(0, normalizeInt64(presence.medkitCount, 0))
    });
    return;
  }

  presence.isUsingMedkit = false;
  presence.medkitUseEndsAtMs = 0;
  presence.medkitCount = Math.min(
    MEDKIT_MAX_COUNT,
    Math.max(0, normalizeInt64(presence.medkitCount, 0)) + 1
  );

  sendMedkitResultToSocket(socket, false, "cancelled", {
    ticketId: ticket.ticketId,
    medkitSeq: Math.max(0, normalizeInt64(presence.medkitSeq, 0)),
    medkitCount: presence.medkitCount
  });

  if (ticket.matchId) {
    touchMatchSession(ticket.matchId);
    broadcastMatchSnapshots(ticket.matchId);
  }
}

function completeMedkitUse(ticket) {
  if (!ticket || !ticket.presence) {
    return;
  }

  const presence = ticket.presence;
  if (!presence.isUsingMedkit) {
    return;
  }

  presence.isUsingMedkit = false;
  presence.medkitUseEndsAtMs = 0;
  const maxHealth = Number.isFinite(presence.maxHealth) ? presence.maxHealth : 100;
  const currentHealth = Number.isFinite(presence.health) ? presence.health : maxHealth;
  presence.health = Math.min(maxHealth, currentHealth + MEDKIT_HEAL_AMOUNT);

  const socket = wsClientsByTicketId.get(ticket.ticketId);
  sendHealToSocket(socket, MEDKIT_HEAL_AMOUNT, presence.medkitSeq);

  if (ticket.matchId) {
    touchMatchSession(ticket.matchId);
    broadcastMatchSnapshots(ticket.matchId);
  }
}

function tickMedkitUses() {
  const nowMs = Date.now();
  for (const ticket of ticketsById.values()) {
    if (!ticket || ticket.status !== "Matched" || !ticket.presence || !ticket.presence.isUsingMedkit) {
      continue;
    }

    if (!ticket.presence.medkitUseEndsAtMs || nowMs < ticket.presence.medkitUseEndsAtMs) {
      continue;
    }

    completeMedkitUse(ticket);
  }
}

function rollPhase1Center(mapCenterX, mapCenterZ, offsetRange) {
  const offset = Math.max(0, normalizeNumber(offsetRange, 10));
  return {
    x: mapCenterX + (((Math.random() * 2) - 1) * offset),
    z: mapCenterZ + (((Math.random() * 2) - 1) * offset)
  };
}

function rollPhase2Center(mapCenterX, mapCenterZ, offsetRange) {
  const offset = Math.max(0, normalizeNumber(offsetRange, ZONE_DEFAULT_PHASE2_CENTER_OFFSET));
  return {
    x: mapCenterX + (((Math.random() * 2) - 1) * offset),
    z: mapCenterZ + (((Math.random() * 2) - 1) * offset)
  };
}

function ensureMatchDamageZone(matchId) {
  if (!matchId) {
    return null;
  }

  if (!matchDamageZonesByMatchId.has(matchId)) {
    const session = matchesById.get(matchId);
    matchDamageZonesByMatchId.set(matchId, {
      configured: false,
      mapCenterX: normalizeNumber(process.env.ZONE_CENTER_X, 0),
      mapCenterZ: normalizeNumber(process.env.ZONE_CENTER_Z, 0),
      phase1CenterX: normalizeNumber(process.env.ZONE_CENTER_X, 0),
      phase1CenterZ: normalizeNumber(process.env.ZONE_CENTER_Z, 0),
      phase2CenterX: normalizeNumber(process.env.ZONE_CENTER_X, 0),
      phase2CenterZ: normalizeNumber(process.env.ZONE_CENTER_Z, 0),
      phase1CenterOffset: ZONE_DEFAULT_PHASE1_CENTER_OFFSET,
      phase2CenterOffset: ZONE_DEFAULT_PHASE2_CENTER_OFFSET,
      initialRadius: ZONE_DEFAULT_INITIAL_RADIUS,
      phase1EndRadius: ZONE_DEFAULT_PHASE1_END_RADIUS,
      finalRadius: ZONE_DEFAULT_FINAL_RADIUS,
      totalShrinkDurationSeconds: ZONE_DEFAULT_TOTAL_SHRINK_DURATION_SEC,
      damageMinPerSecond: ZONE_DEFAULT_DAMAGE_MIN_DPS,
      damageMaxPerSecond: ZONE_DEFAULT_DAMAGE_MAX_DPS,
      damageRampSeconds: ZONE_DEFAULT_DAMAGE_RAMP_SEC,
      startedAtMs: session && session.startedAtMs > 0 ? session.startedAtMs : Date.now(),
      lastBroadcastMs: 0
    });
  }

  return matchDamageZonesByMatchId.get(matchId);
}

function smoothPhaseProgress(normalizedTime) {
  const t = Math.min(1, Math.max(0, normalizedTime));
  return t * t * (3 - 2 * t);
}

function computeZonePhaseProgress(elapsedSeconds, durationSeconds) {
  if (!Number.isFinite(durationSeconds) || durationSeconds <= 0.001) {
    return 1;
  }

  return smoothPhaseProgress(elapsedSeconds / durationSeconds);
}

function computeTwoPhaseDurations(initialRadius, phase1EndRadius, finalRadius, totalShrinkDurationSeconds) {
  const totalDuration = Math.max(0.001, normalizeNumber(totalShrinkDurationSeconds, ZONE_DEFAULT_TOTAL_SHRINK_DURATION_SEC));
  const totalRadiusDelta = Math.max(0.001, initialRadius - finalRadius);
  const phase1RadiusDelta = Math.max(0, initialRadius - phase1EndRadius);
  const phase2RadiusDelta = Math.max(0, phase1EndRadius - finalRadius);
  const phase1DurationSeconds = Math.max(0.001, totalDuration * (phase1RadiusDelta / totalRadiusDelta));
  const phase2DurationSeconds = Math.max(0.001, totalDuration * (phase2RadiusDelta / totalRadiusDelta));
  return { phase1DurationSeconds, phase2DurationSeconds };
}

function resolveTotalShrinkDuration(message, zone) {
  const totalFromMessage = normalizeNumber(message && message.totalShrinkDurationSeconds, 0);
  if (totalFromMessage > 0.001) {
    return Math.max(30, totalFromMessage);
  }

  const legacyPhase1 = normalizeNumber(message && message.phase1DurationSeconds, 0);
  const legacyPhase2 = normalizeNumber(
    message && (message.phase2MoveDurationSeconds || message.phase2DurationSeconds),
    0
  );
  const legacyPhase3 = normalizeNumber(message && message.phase3ShrinkDurationSeconds, 0);
  const legacyTotal = legacyPhase1 + legacyPhase2 + legacyPhase3;
  if (legacyTotal > 0.001) {
    return Math.max(30, legacyTotal);
  }

  return Math.max(30, normalizeNumber(zone && zone.totalShrinkDurationSeconds, ZONE_DEFAULT_TOTAL_SHRINK_DURATION_SEC));
}

function computeTwoPhaseZoneState(zone, elapsedSeconds) {
  if (!zone) {
    return { phase: 1, centerX: 0, centerZ: 0, radius: 0 };
  }

  const initialRadius = Math.max(0, normalizeNumber(zone.initialRadius, ZONE_DEFAULT_INITIAL_RADIUS));
  const phase1EndRadius = Math.max(0, normalizeNumber(zone.phase1EndRadius, ZONE_DEFAULT_PHASE1_END_RADIUS));
  const finalRadius = Math.max(0, normalizeNumber(zone.finalRadius, ZONE_DEFAULT_FINAL_RADIUS));
  const totalShrinkDurationSeconds = Math.max(
    30,
    normalizeNumber(zone.totalShrinkDurationSeconds, ZONE_DEFAULT_TOTAL_SHRINK_DURATION_SEC)
  );
  const { phase1DurationSeconds, phase2DurationSeconds } = computeTwoPhaseDurations(
    initialRadius,
    phase1EndRadius,
    finalRadius,
    totalShrinkDurationSeconds
  );
  const phase1CenterX = normalizeNumber(zone.phase1CenterX, 0);
  const phase1CenterZ = normalizeNumber(zone.phase1CenterZ, 0);
  const phase2CenterX = normalizeNumber(zone.phase2CenterX, phase1CenterX);
  const phase2CenterZ = normalizeNumber(zone.phase2CenterZ, phase1CenterZ);

  if (elapsedSeconds <= phase1DurationSeconds) {
    const progress = computeZonePhaseProgress(elapsedSeconds, phase1DurationSeconds);
    const mapCenterX = normalizeNumber(zone.mapCenterX, phase1CenterX);
    const mapCenterZ = normalizeNumber(zone.mapCenterZ, phase1CenterZ);
    return {
      phase: 1,
      centerX: mapCenterX + ((phase1CenterX - mapCenterX) * progress),
      centerZ: mapCenterZ + ((phase1CenterZ - mapCenterZ) * progress),
      radius: initialRadius + ((phase1EndRadius - initialRadius) * progress)
    };
  }

  const phase2Elapsed = elapsedSeconds - phase1DurationSeconds;
  const progress2 = computeZonePhaseProgress(phase2Elapsed, phase2DurationSeconds);
  return {
    phase: 2,
    centerX: phase1CenterX + ((phase2CenterX - phase1CenterX) * progress2),
    centerZ: phase1CenterZ + ((phase2CenterZ - phase1CenterZ) * progress2),
    radius: phase1EndRadius + ((finalRadius - phase1EndRadius) * progress2)
  };
}

function computeZoneRadius(zone, elapsedSeconds) {
  return computeTwoPhaseZoneState(zone, elapsedSeconds).radius;
}

function computeZoneDamagePerSecond(zone, elapsedSeconds) {
  if (!zone) {
    return 0;
  }

  const ramp = zone.damageRampSeconds <= 0.001
    ? 1
    : Math.min(1, Math.max(0, elapsedSeconds / zone.damageRampSeconds));
  return zone.damageMinPerSecond + ((zone.damageMaxPerSecond - zone.damageMinPerSecond) * ramp);
}

function buildZoneStatePayload(matchId, zone, nowMs) {
  const elapsedSeconds = Math.max(0, (nowMs - zone.startedAtMs) / 1000);
  const liveState = computeTwoPhaseZoneState(zone, elapsedSeconds);
  const damagePerSecond = computeZoneDamagePerSecond(zone, elapsedSeconds);
  return {
    type: "zone_state",
    phase: liveState.phase,
    centerX: liveState.centerX,
    centerZ: liveState.centerZ,
    phase1CenterX: zone.phase1CenterX,
    phase1CenterZ: zone.phase1CenterZ,
    phase2CenterX: zone.phase2CenterX,
    phase2CenterZ: zone.phase2CenterZ,
    mapCenterX: zone.mapCenterX,
    mapCenterZ: zone.mapCenterZ,
    initialRadius: zone.initialRadius,
    phase1EndRadius: zone.phase1EndRadius,
    finalRadius: zone.finalRadius,
    totalShrinkDurationSeconds: zone.totalShrinkDurationSeconds,
    damageMinPerSecond: zone.damageMinPerSecond,
    damageMaxPerSecond: zone.damageMaxPerSecond,
    damageRampSeconds: zone.damageRampSeconds,
    radius: liveState.radius,
    damagePerSecond,
    elapsedSeconds,
    startedAtMs: zone.startedAtMs
  };
}

function sendZoneStateToSocket(socket, matchId) {
  if (!socket || socket.readyState !== WebSocket.OPEN || !matchId) {
    return;
  }

  const zone = ensureMatchDamageZone(matchId);
  if (!zone || !zone.configured) {
    return;
  }

  try {
    socket.send(JSON.stringify(buildZoneStatePayload(matchId, zone, Date.now())));
  } catch {
    // ignored
  }
}

function broadcastZoneState(matchId) {
  if (!matchId) {
    return;
  }

  const zone = ensureMatchDamageZone(matchId);
  if (!zone || !zone.configured) {
    return;
  }

  const payload = JSON.stringify(buildZoneStatePayload(matchId, zone, Date.now()));
  for (const [ticketId, socket] of wsClientsByTicketId.entries()) {
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Matched" || ticket.matchId !== matchId) {
      continue;
    }

    if (!socket || socket.readyState !== WebSocket.OPEN) {
      continue;
    }

    try {
      socket.send(payload);
    } catch {
      // ignored
    }
  }
}

function handleWsRegisterDamageZone(socket, message) {
  const meta = wsMetaBySocket.get(socket);
  if (!meta || !meta.ticketId) {
    return;
  }

  const ticket = ticketsById.get(meta.ticketId);
  if (!ticket || ticket.status !== "Matched" || !ticket.matchId) {
    return;
  }

  const zone = ensureMatchDamageZone(ticket.matchId);
  if (!zone || zone.configured) {
    sendZoneStateToSocket(socket, ticket.matchId);
    return;
  }

  zone.configured = true;
  const mapCenterX = normalizeNumber(message.centerX, zone.mapCenterX);
  const mapCenterZ = normalizeNumber(message.centerZ, zone.mapCenterZ);
  zone.mapCenterX = mapCenterX;
  zone.mapCenterZ = mapCenterZ;
  zone.phase1CenterOffset = Math.max(0, normalizeNumber(message.phase1CenterOffset, ZONE_DEFAULT_PHASE1_CENTER_OFFSET));
  zone.phase2CenterOffset = Math.max(0, normalizeNumber(message.phase2CenterOffset, ZONE_DEFAULT_PHASE2_CENTER_OFFSET));

  const phase1Center = rollPhase1Center(mapCenterX, mapCenterZ, zone.phase1CenterOffset);
  zone.phase1CenterX = phase1Center.x;
  zone.phase1CenterZ = phase1Center.z;

  zone.initialRadius = Math.max(10, normalizeNumber(message.initialRadius, zone.initialRadius));
  zone.phase1EndRadius = Math.max(5, normalizeNumber(message.phase1EndRadius, zone.phase1EndRadius));
  zone.finalRadius = Math.max(0, normalizeNumber(message.finalRadius, zone.finalRadius));
  zone.totalShrinkDurationSeconds = resolveTotalShrinkDuration(message, zone);
  zone.damageMinPerSecond = Math.max(0, normalizeNumber(message.damageMinPerSecond, zone.damageMinPerSecond));
  zone.damageMaxPerSecond = Math.max(
    zone.damageMinPerSecond,
    normalizeNumber(message.damageMaxPerSecond, zone.damageMaxPerSecond)
  );
  zone.damageRampSeconds = Math.max(30, normalizeNumber(message.damageRampSeconds, zone.damageRampSeconds));

  const phase2Center = rollPhase2Center(
    mapCenterX,
    mapCenterZ,
    zone.phase2CenterOffset
  );
  zone.phase2CenterX = phase2Center.x;
  zone.phase2CenterZ = phase2Center.z;

  const session = matchesById.get(ticket.matchId);
  if (session && session.startedAtMs > 0) {
    const br = ensureBattleRoyaleState(session);
    if (br && br.playingStartedAtMs > 0) {
      zone.startedAtMs = br.playingStartedAtMs;
    } else {
      zone.startedAtMs = 0;
    }
  }

  const br = ensureBattleRoyaleState(session);
  if (br) {
    br.mapCenterX = mapCenterX;
    br.mapCenterZ = mapCenterZ;
    if (!br.planePathInitialized) {
      br.planePathAngle = Math.random() * Math.PI * 2;
      br.planePathInitialized = true;
    }
    updateBattleRoyalePlaneRoute(br, mapCenterX, mapCenterZ);
  }

  zone.lastBroadcastMs = 0;
  broadcastZoneState(ticket.matchId);
  touchMatchSession(ticket.matchId);
}

function sendZoneDamageToSocket(socket, targetTicketId, damage, dirX, dirY, dirZ) {
  if (!socket || socket.readyState !== WebSocket.OPEN || damage <= 0) {
    return;
  }

  try {
    socket.send(JSON.stringify({
      type: "damage",
      attackerTicketId: "zone",
      targetTicketId,
      damage,
      dirX,
      dirY,
      dirZ
    }));
  } catch {
    // ignored
  }
}

function flushZoneDamageOutbound(ticket, force) {
  if (!ticket || !ticket.zoneDamageOutbound) {
    return;
  }

  const outbound = ticket.zoneDamageOutbound;
  if (outbound.accumulated <= 0) {
    return;
  }

  const nowMs = Date.now();
  if (!force && nowMs - outbound.lastSentMs < ZONE_DAMAGE_SEND_INTERVAL_MS) {
    return;
  }

  const targetSocket = wsClientsByTicketId.get(ticket.ticketId);
  const damage = outbound.accumulated;
  outbound.accumulated = 0;
  outbound.lastSentMs = nowMs;
  sendZoneDamageToSocket(
    targetSocket,
    ticket.ticketId,
    damage,
    outbound.dirX,
    outbound.dirY,
    outbound.dirZ
  );
}

function applyZoneDamageToTicket(ticket, damage, liveState) {
  if (!ticket || !ticket.presence || damage <= 0 || ticket.presence.isDead) {
    return;
  }

  if (ticket.matchId) {
    const session = matchesById.get(ticket.matchId);
    const br = session ? ensureBattleRoyaleState(session) : null;
    if (br && br.phase === "ending") {
      return;
    }
  }

  const targetPresence = ticket.presence;
  const maxHealth = Number.isFinite(targetPresence.maxHealth) ? targetPresence.maxHealth : 100;
  const currentHealth = Number.isFinite(targetPresence.health) ? targetPresence.health : maxHealth;
  targetPresence.health = Math.max(0, currentHealth - damage);

  let dirX = 0;
  let dirY = 0;
  let dirZ = 1;
  if (liveState && targetPresence.position) {
    const px = normalizeNumber(targetPresence.position.x, 0);
    const pz = normalizeNumber(targetPresence.position.z, 0);
    const dx = liveState.centerX - px;
    const dz = liveState.centerZ - pz;
    const mag = Math.hypot(dx, dz);
    if (mag > 0.0001) {
      dirX = dx / mag;
      dirZ = dz / mag;
    }
  }

  if (!ticket.zoneDamageOutbound) {
    ticket.zoneDamageOutbound = {
      accumulated: 0,
      dirX: 0,
      dirY: 0,
      dirZ: 1,
      lastSentMs: 0
    };
  }

  const outbound = ticket.zoneDamageOutbound;
  outbound.accumulated += damage;
  outbound.dirX = dirX;
  outbound.dirY = dirY;
  outbound.dirZ = dirZ;

  if (targetPresence.health <= 0.001) {
    if (wasKilledByPlayer(ticket)) {
      targetPresence.isDead = true;
      flushZoneDamageOutbound(ticket, true);
      return;
    }

    const wasAlive = !targetPresence.isDead;
    targetPresence.isDead = true;
    targetPresence.deathSeq = Math.max(0, normalizeInt64(targetPresence.deathSeq, 0)) + 1;
    targetPresence.deathFallDirX = dirX;
    targetPresence.deathFallDirY = dirY;
    targetPresence.deathFallDirZ = dirZ;
    flushZoneDamageOutbound(ticket, true);

    if (wasAlive) {
      markTicketDeathCause(ticket, PLAYER_DEATH_CAUSE_ZONE);
      broadcastKillFeed(ticket.matchId, {
        killerTicketId: "zone",
        victimTicketId: ticket.ticketId,
        killerNickname: "Зона",
        victimNickname: resolveTicketNickname(ticket),
        weaponKind: 0,
        cause: "zone",
      });
      onBattleRoyalePlayerDied(ticket);
    }
    return;
  }

  flushZoneDamageOutbound(ticket, false);
}

function tickDamageZones() {
  const nowMs = Date.now();
  const dt = 1 / SERVER_TICK_RATE;

  for (const [matchId, zone] of matchDamageZonesByMatchId.entries()) {
    const session = matchesById.get(matchId);
    if (!session || session.state === "Ended" || !zone || !zone.configured) {
      continue;
    }

    const br = ensureBattleRoyaleState(session);
    if (br && br.phase === "ending") {
      continue;
    }

    if (br && br.phase !== "playing" && br.phase !== "ending") {
      const hasLandedPlayers = br.landedTickets && br.landedTickets.size > 0;
      if (!hasLandedPlayers) {
        continue;
      }
    }

    if (zone.startedAtMs <= 0 && br && br.playingStartedAtMs > 0) {
      zone.startedAtMs = br.playingStartedAtMs;
    }

    if (zone.startedAtMs <= 0) {
      continue;
    }

    const elapsedSeconds = Math.max(0, (nowMs - zone.startedAtMs) / 1000);
    const liveState = computeTwoPhaseZoneState(zone, elapsedSeconds);
    const radius = liveState.radius;
    const damagePerSecond = computeZoneDamagePerSecond(zone, elapsedSeconds);
    const damage = damagePerSecond * dt;

    if (damage > 0 && radius >= 0) {
      const matchTickets = getMatchedTicketsForMatch(matchId);
      for (let i = 0; i < matchTickets.length; i++) {
        const ticket = matchTickets[i];
        if (!ticket || !ticket.presence) {
          continue;
        }

        if (!ticket.presence.hasPose || ticket.presence.isDead || !ticket.presence.position) {
          continue;
        }

        if (br && br.phase === "plane" && !br.landedTickets.has(ticket.ticketId)) {
          continue;
        }

        const px = normalizeNumber(ticket.presence.position.x, 0);
        const pz = normalizeNumber(ticket.presence.position.z, 0);
        const dx = px - liveState.centerX;
        const dz = pz - liveState.centerZ;
        if (Math.hypot(dx, dz) <= radius) {
          continue;
        }

        applyZoneDamageToTicket(ticket, damage, liveState);
      }

      for (let i = 0; i < matchTickets.length; i++) {
        flushZoneDamageOutbound(matchTickets[i], false);
      }
    }

    if (nowMs - zone.lastBroadcastMs >= ZONE_STATE_BROADCAST_INTERVAL_MS) {
      zone.lastBroadcastMs = nowMs;
      broadcastZoneState(matchId);
    }
  }
}

function buildPickupStatePayload(matchId) {
  const state = ensureMatchPickups(matchId);
  const spawns = [];
  if (state) {
    for (const spawn of state.spawns.values()) {
      spawns.push({
        spawnId: spawn.spawnId,
        pickupKind: spawn.pickupKind || "weapon",
        itemId: spawn.itemId || spawn.weaponId || "",
        weaponId: spawn.itemId || spawn.weaponId || "",
        amount: Math.max(1, normalizeInt64(spawn.amount, 1)),
        available: !!spawn.available,
        x: normalizeNumber(spawn.x, 0),
        y: normalizeNumber(spawn.y, 0),
        z: normalizeNumber(spawn.z, 0),
        magAmmo: Number.isFinite(spawn.magAmmo) ? spawn.magAmmo : -1
      });
    }
  }

  return {
    type: "pickup_state",
    spawns
  };
}

function sendPickupStateToSocket(socket, matchId) {
  if (!socket || socket.readyState !== WebSocket.OPEN || !matchId) {
    return;
  }

  try {
    socket.send(JSON.stringify(buildPickupStatePayload(matchId)));
  } catch {
    // ignored
  }
}

function broadcastPickupState(matchId) {
  if (!matchId) {
    return;
  }

  const payload = JSON.stringify(buildPickupStatePayload(matchId));
  for (const [ticketId, socket] of wsClientsByTicketId.entries()) {
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Matched" || ticket.matchId !== matchId) {
      continue;
    }

    if (!socket || socket.readyState !== WebSocket.OPEN) {
      continue;
    }

    try {
      socket.send(payload);
    } catch {
      // ignored
    }
  }
}

function broadcastPickupEvent(matchId, event) {
  if (!matchId || !event) {
    return;
  }

  const payload = JSON.stringify(event);
  for (const [ticketId, socket] of wsClientsByTicketId.entries()) {
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Matched" || ticket.matchId !== matchId) {
      continue;
    }

    if (!socket || socket.readyState !== WebSocket.OPEN) {
      continue;
    }

    try {
      socket.send(payload);
    } catch {
      // ignored
    }
  }
}

function tickPickupRespawns() {
  const nowMs = Date.now();
  for (const [matchId, state] of matchPickupsByMatchId.entries()) {
    if (!state || !state.spawns || state.spawns.size === 0) {
      continue;
    }

    let changed = false;
    for (const spawn of state.spawns.values()) {
      if (!spawn || spawn.available || spawn.respawnAtMs <= 0 || nowMs < spawn.respawnAtMs) {
        continue;
      }

      spawn.available = true;
      spawn.respawnAtMs = 0;
      changed = true;
      broadcastPickupEvent(matchId, {
        type: "pickup_event",
        spawnId: spawn.spawnId,
        ticketId: "",
        weaponPickupSeq: 0,
        pickupKind: spawn.pickupKind || "weapon",
        itemId: spawn.itemId || spawn.weaponId || "",
        weaponId: spawn.itemId || spawn.weaponId || "",
        amount: Math.max(1, normalizeInt64(spawn.amount, 1)),
        available: true,
        x: normalizeNumber(spawn.x, 0),
        y: normalizeNumber(spawn.y, 0),
        z: normalizeNumber(spawn.z, 0)
      });
    }

    if (changed) {
      state.version += 1;
      broadcastPickupState(matchId);
    }
  }
}

function tickAllPlayerMovement() {
  for (const ticketIds of matchedTicketsByMatchId.values()) {
    for (const ticketId of ticketIds) {
      const ticket = ticketsById.get(ticketId);
      if (!ticket || ticket.status !== "Matched" || !ticket.presence || !ticket.presence.hasPose) {
        continue;
      }

      tickPlayerMovement(ticket);
    }
  }
}

function tickPlayerMovement(ticket) {
  const presence = ticket.presence;
  const input = ticket.inputState;
  const dtSec = 1 / SERVER_TICK_RATE;
  const prevX = presence.position.x;
  const prevY = presence.position.y;
  const prevZ = presence.position.z;

  if (presence.isDead) {
    presence.sampleTick = currentServerTick;
    pushStateHistory(ticket);
    pushPoseHistory(ticket, buildPoseHistoryEntry(ticket));
    return;
  }

  if (input && input.inputAuth) {
    // XZ/Y come from the client pose (CharacterController handles walls). Tick loop only tracks velocity/history.
    presence.yaw = Number.isFinite(input.yaw) ? input.yaw : presence.yaw;
    presence.isGrounded = !!input.isGrounded;
    presence.isCrouching = !!input.isCrouching;
    presence.isSprinting = !!input.isSprinting;
    presence.jumpState = Number.isFinite(input.jumpState) ? input.jumpState : presence.jumpState;
  }

  presence.velocityX = (presence.position.x - prevX) / dtSec;
  presence.velocityY = (presence.position.y - prevY) / dtSec;
  presence.velocityZ = (presence.position.z - prevZ) / dtSec;
  presence.sampleTick = currentServerTick;
  pushStateHistory(ticket);
  pushPoseHistory(ticket, buildPoseHistoryEntry(ticket));
}

function buildPoseHistoryEntry(ticket) {
  const presence = ticket.presence;
  return {
    position: {
      x: presence.position.x,
      y: presence.position.y,
      z: presence.position.z
    },
    yaw: presence.yaw,
    lookPitch: presence.lookPitch || 0,
    isCrouching: !!presence.isCrouching,
    sampleTick: currentServerTick,
    timeMs: Date.now()
  };
}

function computeHorizontalDelta(input, dtSec) {
  const yawRad = (normalizeNumber(input.yaw, 0) * Math.PI) / 180;
  const forwardX = Math.sin(yawRad);
  const forwardZ = Math.cos(yawRad);
  const rightX = Math.cos(yawRad);
  const rightZ = -Math.sin(yawRad);

  let inputX = normalizeNumber(input.moveInputX, 0);
  let inputZ = normalizeNumber(input.moveInputZ, 0);
  const rawMag = Math.hypot(inputX, inputZ);
  if (rawMag > 1) {
    inputX /= rawMag;
    inputZ /= rawMag;
  }

  inputX *= PLAYER_SIDE_SPEED_MULTIPLIER;
  if (inputZ < 0) {
    inputZ *= PLAYER_BACKWARD_SPEED_MULTIPLIER;
  }

  let moveDirX = rightX * inputX + forwardX * inputZ;
  let moveDirZ = rightZ * inputX + forwardZ * inputZ;
  const moveDirMag = Math.hypot(moveDirX, moveDirZ);
  if (moveDirMag > 1) {
    moveDirX /= moveDirMag;
    moveDirZ /= moveDirMag;
  }

  let speedMultiplier = 1;
  if (input.isCrouching) {
    speedMultiplier = PLAYER_CROUCH_MULTIPLIER;
  } else if (
    input.isSprinting &&
    normalizeNumber(input.moveInputZ, 0) > PLAYER_SPRINT_MIN_FORWARD &&
    rawMag > 0.12
  ) {
    speedMultiplier = PLAYER_SPRINT_MULTIPLIER;
  }

  const horizontalSpeed = PLAYER_MOVE_SPEED * speedMultiplier;
  return {
    dx: moveDirX * horizontalSpeed * dtSec,
    dz: moveDirZ * horizontalSpeed * dtSec
  };
}

function pushStateHistory(ticket) {
  if (!ticket || !ticket.presence || !ticket.presence.hasPose) {
    return;
  }

  if (!Array.isArray(ticket.stateHistory)) {
    ticket.stateHistory = [];
  }

  const presence = ticket.presence;
  ticket.stateHistory.push({
    sampleTick: currentServerTick,
    x: presence.position.x,
    y: presence.position.y,
    z: presence.position.z,
    yaw: presence.yaw || 0,
    velX: Number.isFinite(presence.velocityX) ? presence.velocityX : 0,
    velY: Number.isFinite(presence.velocityY) ? presence.velocityY : 0,
    velZ: Number.isFinite(presence.velocityZ) ? presence.velocityZ : 0
  });

  const last = ticket.stateHistory.length >= 2
    ? ticket.stateHistory[ticket.stateHistory.length - 2]
    : null;
  if (last &&
      last.sampleTick === currentServerTick - 1 &&
      last.x === presence.position.x &&
      last.y === presence.position.y &&
      last.z === presence.position.z &&
      Math.abs(last.yaw - (presence.yaw || 0)) < 0.01) {
    ticket.stateHistory.pop();
    return;
  }

  const maxEntries = SNAPSHOT_HISTORY_SAMPLES * 3;
  while (ticket.stateHistory.length > maxEntries) {
    ticket.stateHistory.shift();
  }
}

function getBroadcastStateHistory(ticket) {
  if (!ticket || !Array.isArray(ticket.stateHistory)) {
    return [];
  }

  return ticket.stateHistory.slice(-SNAPSHOT_HISTORY_SAMPLES).map((entry) => ({
    sampleTick: entry.sampleTick,
    x: entry.x,
    y: entry.y,
    z: entry.z,
    yaw: entry.yaw,
    velX: entry.velX,
    velY: entry.velY,
    velZ: entry.velZ
  }));
}

function pushPoseHistory(ticket, entry) {
  if (!ticket) {
    return;
  }

  if (!Array.isArray(ticket.poseHistory)) {
    ticket.poseHistory = [];
  }

  ticket.poseHistory.push(entry);
  const cutoff = Date.now() - POSE_HISTORY_KEEP_MS;
  while (ticket.poseHistory.length > 0 && ticket.poseHistory[0].timeMs < cutoff) {
    ticket.poseHistory.shift();
  }

  const maxEntries = Math.ceil((POSE_HISTORY_KEEP_MS / 1000) * SERVER_TICK_RATE) + 8;
  while (ticket.poseHistory.length > maxEntries) {
    ticket.poseHistory.shift();
  }
}

function clampPositionToMovement(prevPresence, nextPosition, sampleTick) {
  if (!prevPresence || !prevPresence.hasPose || !prevPresence.position || !nextPosition) {
    return nextPosition;
  }

  if (prevPresence.isUsingMedkit) {
    return {
      x: prevPresence.position.x,
      y: nextPosition.y,
      z: prevPresence.position.z
    };
  }

  const prevTick = prevPresence.sampleTick || (sampleTick - 1);
  const dtTicks = Math.max(1, sampleTick - prevTick);
  const tickDtSec = dtTicks / SERVER_TICK_RATE;
  const lastMs = prevPresence.serverSampleTimeMs || prevPresence.sampleTimeMs || 0;
  const wallDtSec = lastMs > 0
    ? Math.max(1 / SERVER_TICK_RATE, (Date.now() - lastMs) / 1000)
    : tickDtSec;
  const dtSec = Math.max(tickDtSec, wallDtSec);
  const maxHorizStep = MAX_PLAYER_SPEED * dtSec * 1.35;
  const dx = nextPosition.x - prevPresence.position.x;
  const dz = nextPosition.z - prevPresence.position.z;
  const horiz = Math.hypot(dx, dz);
  if (horiz <= maxHorizStep) {
    return nextPosition;
  }

  const scale = maxHorizStep / Math.max(0.0001, horiz);
  return {
    x: prevPresence.position.x + dx * scale,
    y: nextPosition.y,
    z: prevPresence.position.z + dz * scale
  };
}

function acceptAuthorizedClientPosition(prevPresence, nextPosition) {
  const next = normalizePosition(nextPosition);
  if (!prevPresence || !prevPresence.hasPose || !prevPresence.position) {
    return next;
  }

  if (prevPresence.isUsingMedkit) {
    return {
      x: prevPresence.position.x,
      y: next.y,
      z: prevPresence.position.z
    };
  }

  const dx = next.x - prevPresence.position.x;
  const dz = next.z - prevPresence.position.z;
  const horiz = Math.hypot(dx, dz);
  const maxTeleportStep = MAX_PLAYER_SPEED * 0.35;
  if (horiz <= maxTeleportStep) {
    return next;
  }

  return clampPositionToMovement(prevPresence, next, currentServerTick);
}

function getPoseAtOrBeforeTick(ticket, tick) {
  if (!ticket) {
    return null;
  }

  let best = null;
  const history = Array.isArray(ticket.poseHistory) ? ticket.poseHistory : [];
  for (let i = 0; i < history.length; i++) {
    const entry = history[i];
    if (!entry || !Number.isFinite(entry.sampleTick)) {
      continue;
    }

    if (entry.sampleTick <= tick && (!best || entry.sampleTick > best.sampleTick)) {
      best = entry;
    }
  }

  if (best) {
    return best;
  }

  if (ticket.presence && ticket.presence.hasPose) {
    return ticket.presence;
  }

  return null;
}

function resolveServerDamage(message) {
  const hitZone = typeof message.hitZone === "string" ? message.hitZone.trim().toLowerCase() : "";
  if (Object.prototype.hasOwnProperty.call(DAMAGE_BY_HIT_ZONE, hitZone)) {
    return DAMAGE_BY_HIT_ZONE[hitZone];
  }

  return Math.max(0, Math.min(1000, normalizeNumber(message.damage, 25)));
}

function validateLagCompensatedHit(attacker, target, message) {
  const maxRewindTicks = Math.max(8, Math.ceil(SERVER_TICK_RATE * 0.75));
  const requestedTick = normalizeInt64(message.shotTick, currentServerTick);
  const shotTick = Math.max(
    currentServerTick - maxRewindTicks,
    Math.min(currentServerTick, requestedTick)
  );

  const victimPose = getPoseAtOrBeforeTick(target, shotTick);
  const attackerPose = getPoseAtOrBeforeTick(attacker, shotTick);
  if (!victimPose || !attackerPose || !victimPose.position || !attackerPose.position) {
    return false;
  }

  const hx = normalizeNumber(message.hitX, 0);
  const hy = normalizeNumber(message.hitY, 0);
  const hz = normalizeNumber(message.hitZ, 0);
  const vx = victimPose.position.x;
  const vy = victimPose.position.y;
  const vz = victimPose.position.z;
  const victimCenterY = vy + (victimPose.isCrouching ? 0.65 : 0.92);
  const dx = hx - vx;
  const dy = hy - victimCenterY;
  const dz = hz - vz;
  if (Math.hypot(dx, dy, dz) > PLAYER_HIT_RADIUS) {
    return false;
  }

  const ax = attackerPose.position.x;
  const ay = attackerPose.position.y + 1.55;
  const az = attackerPose.position.z;
  const toHitX = hx - ax;
  const toHitY = hy - ay;
  const toHitZ = hz - az;
  const shotDist = Math.hypot(toHitX, toHitY, toHitZ);
  if (shotDist > MAX_WEAPON_RANGE || shotDist < 0.05) {
    return false;
  }

  let dirX = normalizeNumber(message.dirX, 0);
  let dirY = normalizeNumber(message.dirY, 0);
  let dirZ = normalizeNumber(message.dirZ, 0);
  const dirMag = Math.hypot(dirX, dirY, dirZ);
  if (dirMag <= 0.0001) {
    return false;
  }

  dirX /= dirMag;
  dirY /= dirMag;
  dirZ /= dirMag;
  const dot = (dirX * (toHitX / shotDist)) + (dirY * (toHitY / shotDist)) + (dirZ * (toHitZ / shotDist));
  return dot >= 0.82;
}

function handleWsHit(socket, message) {
  const meta = wsMetaBySocket.get(socket);
  if (!meta || !meta.ticketId) {
    return;
  }

  const attackerTicket = ticketsById.get(meta.ticketId);
  if (!attackerTicket || attackerTicket.status !== "Matched" || !attackerTicket.matchId) {
    return;
  }

  if (!isBattleRoyaleCombatAllowed(attackerTicket)) {
    return;
  }

  const rawTargetTicketId = typeof message.targetTicketId === "string" ? message.targetTicketId.trim() : "";
  if (!rawTargetTicketId || rawTargetTicketId === attackerTicket.ticketId) {
    return;
  }

  const targetTicket = ticketsById.get(rawTargetTicketId);
  if (!targetTicket || targetTicket.status !== "Matched" || targetTicket.matchId !== attackerTicket.matchId) {
    return;
  }

  const damage = resolveServerDamage(message);
  if (damage <= 0) {
    return;
  }

  if (!validateLagCompensatedHit(attackerTicket, targetTicket, message)) {
    if (DEBUG_REALTIME) {
      console.log(`[rt][hit-reject] attacker=${attackerTicket.ticketId} target=${targetTicket.ticketId}`);
    }
    return;
  }

  let dirX = normalizeNumber(message.dirX, 0);
  let dirY = normalizeNumber(message.dirY, 0);
  let dirZ = normalizeNumber(message.dirZ, 0);
  const dirMag = Math.sqrt(dirX * dirX + dirY * dirY + dirZ * dirZ);
  if (dirMag > 0.0001) {
    dirX /= dirMag;
    dirY /= dirMag;
    dirZ /= dirMag;
  } else {
    dirX = 0;
    dirY = 0;
    dirZ = 1;
  }

  if (!targetTicket.presence) {
    targetTicket.presence = createDefaultPresence(currentServerTick, Date.now());
  }

  const targetPresence = targetTicket.presence;
  const maxHealth = Number.isFinite(targetPresence.maxHealth) ? targetPresence.maxHealth : 100;
  const currentHealth = Number.isFinite(targetPresence.health) ? targetPresence.health : maxHealth;
  const wasAlive = currentHealth > 0.001;
  targetPresence.health = Math.max(0, currentHealth - damage);
  if (targetPresence.health <= 0.001 && wasAlive) {
    targetPresence.isDead = true;
    targetPresence.deathSeq = Math.max(0, normalizeInt64(targetPresence.deathSeq, 0)) + 1;
    markTicketDeathCause(targetTicket, PLAYER_DEATH_CAUSE_PLAYER);
    recordBattleRoyaleKill(attackerTicket);
    broadcastKillFeed(attackerTicket.matchId, {
      killerTicketId: attackerTicket.ticketId,
      victimTicketId: targetTicket.ticketId,
      killerNickname: resolveTicketNickname(attackerTicket),
      victimNickname: resolveTicketNickname(targetTicket),
      weaponKind: Math.max(0, Math.min(WEAPON_KIND_MAX, normalizeInt64(attackerTicket.presence?.weaponKind, 0))),
      cause: "player",
    });
    onBattleRoyalePlayerDied(targetTicket);
  }

  const targetSocket = wsClientsByTicketId.get(targetTicket.ticketId);
  if (!targetSocket || targetSocket.readyState !== WebSocket.OPEN) {
    return;
  }

  try {
    targetSocket.send(JSON.stringify({
      type: "damage",
      attackerTicketId: attackerTicket.ticketId,
      targetTicketId: targetTicket.ticketId,
      damage,
      dirX,
      dirY,
      dirZ
    }));
  } catch {
    // ignored
  }
}

function handleWsDisconnect(socket) {
  const meta = wsMetaBySocket.get(socket);
  wsMetaBySocket.delete(socket);
  if (!meta || !meta.ticketId) {
    return;
  }

  const registered = wsClientsByTicketId.get(meta.ticketId);
  if (registered === socket) {
    wsClientsByTicketId.delete(meta.ticketId);
    const ticket = ticketsById.get(meta.ticketId);
    if (ticket) {
      handleBattleRoyalePlayerLeft(meta.ticketId);
      const closeTelemetry = ensureTicketTelemetry(ticket);
      closeTelemetry.wsCloseCount += 1;
      closeTelemetry.lastWsCloseMs = Date.now();
      touchMatchSession(ticket.matchId);
    }
    if (DEBUG_REALTIME) {
      console.log(`[rt][disconnect] ticket=${meta.ticketId}`);
    }
  }
}

function encodeSnapshotBinary(payload) {
  if (!payload) {
    return null;
  }

  try {
    const chunks = [];
    const header = Buffer.alloc(11);
    header.write("RTS1", 0, 4, "ascii");
    header.writeUInt8(12, 4);
    header.writeUInt32LE(payload.serverTick >>> 0, 5);
    header.writeUInt16LE(payload.serverTickRate >>> 0, 9);
    chunks.push(header);

    const selfAuth = payload.selfAuthoritative;
    const hasSelfAuth = !!(selfAuth && selfAuth.position);
    chunks.push(Buffer.from([hasSelfAuth ? 1 : 0]));

    if (hasSelfAuth) {
      const selfBuf = Buffer.alloc(20);
      selfBuf.writeUInt32LE((selfAuth.sampleTick || payload.serverTick) >>> 0, 0);
      selfBuf.writeFloatLE(selfAuth.position.x, 4);
      selfBuf.writeFloatLE(selfAuth.position.y, 8);
      selfBuf.writeFloatLE(selfAuth.position.z, 12);
      selfBuf.writeFloatLE(selfAuth.yaw || 0, 16);
      chunks.push(selfBuf);
    }

    const players = Array.isArray(payload.players) ? payload.players : [];
    chunks.push(Buffer.from([Math.min(255, players.length)]));

    for (let i = 0; i < players.length; i++) {
      const player = players[i];
      const ticketId = typeof player.ticketId === "string" ? player.ticketId : "";
      const ticketBytes = Buffer.from(ticketId, "utf8");
      chunks.push(Buffer.from([Math.min(255, ticketBytes.length)]));
      chunks.push(ticketBytes);
      const modelName = typeof player.characterModel === "string" ? player.characterModel : "";
      const modelBytes = Buffer.from(modelName, "utf8");
      chunks.push(Buffer.from([Math.min(255, modelBytes.length)]));
      chunks.push(modelBytes);
      writeSkinBlockChunks(chunks, player);
      writeWeaponSkinBlockChunks(chunks, player);

      const pos = player.position || { x: 0, y: 0, z: 0 };
      const body = Buffer.alloc(35);
      body.writeUInt32LE((player.sampleTick || payload.serverTick) >>> 0, 0);
      body.writeFloatLE(pos.x, 4);
      body.writeFloatLE(pos.y, 8);
      body.writeFloatLE(pos.z, 12);
      body.writeFloatLE(player.yaw || 0, 16);
      body.writeFloatLE(player.velX || 0, 20);
      body.writeFloatLE(player.velY || 0, 24);
      body.writeFloatLE(player.velZ || 0, 28);
      let flags2 = 0;
      if (player.isDead) flags2 |= 1;
      if (player.isGrounded !== false) flags2 |= 2;
      if (player.isCrouching) flags2 |= 4;
      if (player.isSprinting) flags2 |= 8;
      if (player.isAiming) flags2 |= 16;
      if (player.isHolstered) flags2 |= 32;
      if (player.hasWeapon) flags2 |= 64;
      if (player.isUsingMedkit) flags2 |= 128;
      if (player.isSwimming) flags2 |= 256;
      body.writeUInt16LE(flags2, 32);
      body.writeUInt8(Math.max(0, Math.min(2, player.jumpState || 0)), 34);
      chunks.push(body);

      const meta = Buffer.alloc(106);
      meta.writeFloatLE(player.lookPitch || 0, 0);
      meta.writeUInt32LE((player.shotSeq || 0) >>> 0, 4);
      meta.writeUInt32LE((player.reloadSeq || 0) >>> 0, 8);
      meta.writeUInt32LE((player.hitPlayerSeq || 0) >>> 0, 12);
      meta.writeUInt32LE((player.footstepSeq || 0) >>> 0, 16);
      meta.writeFloatLE(player.wallAvoidBlend || 0, 20);
      meta.writeFloatLE(player.animSpeed || 0, 24);
      meta.writeFloatLE(player.animPhase || 0, 28);
      meta.writeUInt32LE((player.deathSeq || 0) >>> 0, 32);
      meta.writeFloatLE(player.deathFallDirX || 0, 36);
      meta.writeFloatLE(player.deathFallDirY || 0, 40);
      meta.writeFloatLE(player.deathFallDirZ || 0, 44);
      meta.writeFloatLE(player.shotOriginX || 0, 48);
      meta.writeFloatLE(player.shotOriginY || 0, 52);
      meta.writeFloatLE(player.shotOriginZ || 0, 56);
      meta.writeFloatLE(player.shotDirX || 0, 60);
      meta.writeFloatLE(player.shotDirY || 0, 64);
      meta.writeFloatLE(player.shotDirZ || 0, 68);
      meta.writeFloatLE(player.moveInputX || 0, 72);
      meta.writeFloatLE(player.moveInputZ || 0, 76);
      meta.writeFloatLE(player.shotEndX || 0, 80);
      meta.writeFloatLE(player.shotEndY || 0, 84);
      meta.writeFloatLE(player.shotEndZ || 0, 88);
      meta.writeUInt8(player.shotHasEndPoint ? 1 : 0, 92);
      meta.writeUInt32LE((player.weaponPickupSeq || 0) >>> 0, 93);
      meta.writeFloatLE(player.medkitRemainingSeconds || 0, 97);
      meta.writeUInt8(Math.max(0, Math.min(255, player.medkitCount || 0)), 101);
      meta.writeUInt8(Math.max(0, Math.min(WEAPON_KIND_MAX, player.weaponKind || 0)), 102);
      meta.writeUInt8(Math.max(0, Math.min(255, player.weaponSlot0Kind ?? WEAPON_SLOT_EMPTY)), 103);
      meta.writeUInt8(Math.max(0, Math.min(255, player.weaponSlot1Kind ?? WEAPON_SLOT_EMPTY)), 104);
      meta.writeUInt8(Math.max(0, Math.min(255, player.activeWeaponSlot ?? WEAPON_SLOT_EMPTY)), 105);
      chunks.push(meta);

      const recentShots = Array.isArray(player.recentShots) ? player.recentShots.slice(-8) : [];
      const ringCount = Math.min(255, recentShots.length);
      chunks.push(Buffer.from([ringCount]));
      for (let s = 0; s < ringCount; s++) {
        const ev = recentShots[s] || {};
        const shotBuf = Buffer.alloc(41);
        shotBuf.writeUInt32LE((ev.seq || 0) >>> 0, 0);
        shotBuf.writeFloatLE(ev.shotOriginX || 0, 4);
        shotBuf.writeFloatLE(ev.shotOriginY || 0, 8);
        shotBuf.writeFloatLE(ev.shotOriginZ || 0, 12);
        shotBuf.writeFloatLE(ev.shotDirX || 0, 16);
        shotBuf.writeFloatLE(ev.shotDirY || 0, 20);
        shotBuf.writeFloatLE(ev.shotDirZ || 0, 24);
        shotBuf.writeFloatLE(ev.shotEndX || 0, 28);
        shotBuf.writeFloatLE(ev.shotEndY || 0, 32);
        shotBuf.writeFloatLE(ev.shotEndZ || 0, 36);
        shotBuf.writeUInt8(ev.shotHasEndPoint ? 1 : 0, 40);
        chunks.push(shotBuf);
      }

      const history = Array.isArray(player.history) ? player.history : [];
      const historyCount = Math.min(255, history.length);
      chunks.push(Buffer.from([historyCount]));
      for (let h = 0; h < historyCount; h++) {
        const sample = history[h];
        const sampleBuf = Buffer.alloc(32);
        sampleBuf.writeUInt32LE((sample.sampleTick || 0) >>> 0, 0);
        sampleBuf.writeFloatLE(sample.x || 0, 4);
        sampleBuf.writeFloatLE(sample.y || 0, 8);
        sampleBuf.writeFloatLE(sample.z || 0, 12);
        sampleBuf.writeFloatLE(sample.yaw || 0, 16);
        sampleBuf.writeFloatLE(sample.velX || 0, 20);
        sampleBuf.writeFloatLE(sample.velY || 0, 24);
        sampleBuf.writeFloatLE(sample.velZ || 0, 28);
        chunks.push(sampleBuf);
      }
    }

    return Buffer.concat(chunks);
  } catch {
    return null;
  }
}

function broadcastRealtimeSnapshots() {
  synchronizeActiveMatchCounts();

  for (const [ownerTicketId, socket] of wsClientsByTicketId.entries()) {
    if (!socket || socket.readyState !== WebSocket.OPEN) {
      continue;
    }

    const ownerTicket = ticketsById.get(ownerTicketId);
    if (!ownerTicket || ownerTicket.status !== "Matched") {
      continue;
    }

    try {
      sendSnapshotToSocket(ownerTicketId, socket);
    } catch {
      // ignored; close/error handlers will clean up
    }
  }
}

function sendSnapshotToSocket(ownerTicketId, socket) {
  if (!socket || socket.readyState !== WebSocket.OPEN) {
    return;
  }

  const ownerTicket = ticketsById.get(ownerTicketId);
  if (!ownerTicket || ownerTicket.status !== "Matched" || !ownerTicket.matchId) {
    return;
  }
  const others = collectRealtimePlayersForMatch(ownerTicket.matchId, ownerTicketId);
  if (DEBUG_REALTIME) {
    const nowMs = Date.now();
    const last = lastSnapshotDebugByOwner.get(ownerTicketId) || 0;
    if (nowMs - last >= 1000) {
      lastSnapshotDebugByOwner.set(ownerTicketId, nowMs);
      console.log(`[rt][snapshot] owner=${ownerTicketId} match=${ownerTicket.matchId} others=${others.length}`);
    }
  }

  const payload = {
    type: "snapshot",
    serverTick: currentServerTick,
    serverTickRate: SERVER_TICK_RATE,
    players: others
  };

  if (ownerTicket.presence && ownerTicket.presence.hasPose && ownerTicket.presence.position) {
    payload.selfAuthoritative = {
      position: ownerTicket.presence.position,
      yaw: ownerTicket.presence.yaw || 0,
      sampleTick: ownerTicket.presence.sampleTick || currentServerTick
    };
  }

  if (USE_BINARY_SNAPSHOTS) {
    const binaryPayload = encodeSnapshotBinary(payload);
    if (binaryPayload) {
      socket.send(binaryPayload);
      return;
    }
  }

  socket.send(JSON.stringify(payload));
}

function collectRealtimePlayersForMatch(matchId, ownerTicketId) {
  const players = [];
  if (!matchId) {
    return players;
  }

  const nowMs = Date.now();
  const matchTickets = getMatchedTicketsForMatch(matchId);
  for (let i = 0; i < matchTickets.length; i++) {
    const ticket = matchTickets[i];
    if (!ticket || !ticket.presence) {
      continue;
    }

    if (ticket.ticketId === ownerTicketId) {
      continue;
    }

    // Skip players that have not sent a real pose yet.
    // Prevents remote spawn at default zero before first update arrives.
    if (!ticket.presence.hasPose) {
      continue;
    }

    const stillInConnectGrace = ticket.matchedAtMs > 0 &&
      (nowMs - ticket.matchedAtMs) <= MATCH_CONNECT_GRACE_SECONDS * 1000;
    const hasRecentPresence = ticket.presence.lastSeenMs > 0 &&
      (nowMs - ticket.presence.lastSeenMs) <= (PRESENCE_TIMEOUT_SECONDS * 1000 * 2);
    const hasLiveSocket = isTicketSocketLive(ticket.ticketId);
    if (!stillInConnectGrace && !hasRecentPresence && !hasLiveSocket) {
      continue;
    }

    players.push({
      ticketId: ticket.ticketId,
      position: ticket.presence.position,
      yaw: ticket.presence.yaw,
      lookPitch: ticket.presence.lookPitch || 0,
      characterModel: ticket.presence.characterModel || "",
      skinShirt: ticket.presence.skinShirt || "",
      skinPants: ticket.presence.skinPants || "",
      skinBoots: ticket.presence.skinBoots || "",
      skinGloves: ticket.presence.skinGloves || "",
      skinFace: ticket.presence.skinFace || "",
      skinHair: ticket.presence.skinHair || "",
      skinWeaponAssault: ticket.presence.skinWeaponAssault || "",
      skinWeaponSniper: ticket.presence.skinWeaponSniper || "",
      skinWeaponPistol: ticket.presence.skinWeaponPistol || "",
      skinWeaponMp7: ticket.presence.skinWeaponMp7 || "",
      shotSeq: Number.isFinite(ticket.presence.shotSeq) ? ticket.presence.shotSeq : 0,
      shotOriginX: Number.isFinite(ticket.presence.shotOriginX) ? ticket.presence.shotOriginX : 0,
      shotOriginY: Number.isFinite(ticket.presence.shotOriginY) ? ticket.presence.shotOriginY : 0,
      shotOriginZ: Number.isFinite(ticket.presence.shotOriginZ) ? ticket.presence.shotOriginZ : 0,
      shotDirX: Number.isFinite(ticket.presence.shotDirX) ? ticket.presence.shotDirX : 0,
      shotDirY: Number.isFinite(ticket.presence.shotDirY) ? ticket.presence.shotDirY : 0,
      shotDirZ: Number.isFinite(ticket.presence.shotDirZ) ? ticket.presence.shotDirZ : 0,
      shotEndX: Number.isFinite(ticket.presence.shotEndX) ? ticket.presence.shotEndX : 0,
      shotEndY: Number.isFinite(ticket.presence.shotEndY) ? ticket.presence.shotEndY : 0,
      shotEndZ: Number.isFinite(ticket.presence.shotEndZ) ? ticket.presence.shotEndZ : 0,
      shotHasEndPoint: !!ticket.presence.shotHasEndPoint,
      recentShots: Array.isArray(ticket.presence.shotRing) ? ticket.presence.shotRing.slice(-8) : [],
      reloadSeq: Number.isFinite(ticket.presence.reloadSeq) ? ticket.presence.reloadSeq : 0,
      hitPlayerSeq: Number.isFinite(ticket.presence.hitPlayerSeq) ? ticket.presence.hitPlayerSeq : 0,
      footstepSeq: Number.isFinite(ticket.presence.footstepSeq) ? ticket.presence.footstepSeq : 0,
      isCrouching: !!ticket.presence.isCrouching,
      isSprinting: !!ticket.presence.isSprinting,
      isSwimming: !!ticket.presence.isSwimming,
      wallAvoidBlend: Number.isFinite(ticket.presence.wallAvoidBlend) ? ticket.presence.wallAvoidBlend : 0,
      isDead: !!ticket.presence.isDead,
      deathSeq: Number.isFinite(ticket.presence.deathSeq) ? ticket.presence.deathSeq : 0,
      deathFallDirX: Number.isFinite(ticket.presence.deathFallDirX) ? ticket.presence.deathFallDirX : 0,
      deathFallDirY: Number.isFinite(ticket.presence.deathFallDirY) ? ticket.presence.deathFallDirY : 0,
      deathFallDirZ: Number.isFinite(ticket.presence.deathFallDirZ) ? ticket.presence.deathFallDirZ : 0,
      isAiming: !!ticket.presence.isAiming,
      isHolstered: !!ticket.presence.isHolstered,
      hasWeapon: !!ticket.presence.hasWeapon,
      weaponKind: Number.isFinite(ticket.presence.weaponKind) ? ticket.presence.weaponKind : 0,
      weaponSlot0Kind: Number.isFinite(ticket.presence.weaponSlot0Kind)
        ? ticket.presence.weaponSlot0Kind
        : WEAPON_SLOT_EMPTY,
      weaponSlot1Kind: Number.isFinite(ticket.presence.weaponSlot1Kind)
        ? ticket.presence.weaponSlot1Kind
        : WEAPON_SLOT_EMPTY,
      activeWeaponSlot: Number.isFinite(ticket.presence.activeWeaponSlot)
        ? ticket.presence.activeWeaponSlot
        : WEAPON_SLOT_EMPTY,
      isUsingMedkit: !!ticket.presence.isUsingMedkit,
      medkitRemainingSeconds: getMedkitRemainingSeconds(ticket.presence),
      medkitCount: Math.max(0, normalizeInt64(ticket.presence.medkitCount, 0)),
      weaponPickupSeq: Number.isFinite(ticket.presence.weaponPickupSeq) ? ticket.presence.weaponPickupSeq : 0,
      isGrounded: ticket.presence.isGrounded !== false,
      jumpState: Number.isFinite(ticket.presence.jumpState) ? ticket.presence.jumpState : 0,
      animPhase: Number.isFinite(ticket.presence.animPhase) ? ticket.presence.animPhase : 0,
      animSpeed: ticket.presence.isUsingMedkit ? 0 : (ticket.presence.animSpeed || 0),
      velX: Number.isFinite(ticket.presence.velocityX) ? ticket.presence.velocityX : 0,
      velY: Number.isFinite(ticket.presence.velocityY) ? ticket.presence.velocityY : 0,
      velZ: Number.isFinite(ticket.presence.velocityZ) ? ticket.presence.velocityZ : 0,
      moveInputX: ticket.presence.isUsingMedkit
        ? 0
        : (ticket.inputState?.inputAuth ? normalizeNumber(ticket.inputState.moveInputX, 0) : 0),
      moveInputZ: ticket.presence.isUsingMedkit
        ? 0
        : (ticket.inputState?.inputAuth ? normalizeNumber(ticket.inputState.moveInputZ, 0) : 0),
      sampleTick: ticket.presence.sampleTick || currentServerTick,
      sampleTimeMs: ticket.presence.sampleTimeMs || ticket.presence.serverSampleTimeMs || Date.now(),
      killCount: getBattleRoyaleKillCountForTicket(ticket.matchId, ticket.ticketId),
      history: getBroadcastStateHistory(ticket)
    });
  }

  return players;
}

function broadcastMatchSnapshots(matchId) {
  if (!matchId) {
    return;
  }

  const nowMs = Date.now();
  const lastAtMs = lastMatchSnapshotBroadcastAtMs.get(matchId) || 0;
  // Throttle pose-triggered bursts; periodic broadcastRealtimeSnapshots() still runs every server tick.
  if (nowMs - lastAtMs < 16) {
    return;
  }
  lastMatchSnapshotBroadcastAtMs.set(matchId, nowMs);

  for (const [ticketId, socket] of wsClientsByTicketId.entries()) {
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Matched" || ticket.matchId !== matchId) {
      continue;
    }
    if (!socket || socket.readyState !== WebSocket.OPEN) {
      continue;
    }

    try {
      sendSnapshotToSocket(ticketId, socket);
    } catch {
      // ignored
    }
  }
}

function synchronizeActiveMatchCounts() {
  const now = Date.now();
  let firstActiveSession = null;

  for (const session of matchesById.values()) {
    if (!session || session.state === "Ended") {
      continue;
    }

    const matchedTicketIds = [];
    for (const ticketId of session.ticketIds) {
      const ticket = ticketsById.get(ticketId);
      if (!ticket || ticket.status !== "Matched") {
        continue;
      }

      const stillInConnectGrace = ticket.matchedAtMs > 0 &&
        (now - ticket.matchedAtMs) <= MATCH_CONNECT_GRACE_SECONDS * 1000;
      const hasRecentPresence = ticket.presence &&
        ticket.presence.lastSeenMs > 0 &&
        (now - ticket.presence.lastSeenMs) <= PRESENCE_TIMEOUT_SECONDS * 1000;
      const hasLiveSocket = isTicketSocketLive(ticketId);

      if (stillInConnectGrace || hasRecentPresence || hasLiveSocket) {
        matchedTicketIds.push(ticketId);
      } else {
        ticket.status = "Disconnected";
        dropTicketRealtimeConnection(ticketId, "ticket disconnected");
      }
    }

    session.ticketIds = new Set(matchedTicketIds);
    session.lastActivityMs = now;
    if (matchedTicketIds.length > 0 && session.state === "Open") {
      session.state = "Active";
    }
    if (matchedTicketIds.length === 0) {
      session.state = "Ended";
      session.endedAtMs = now;
    } else if (firstActiveSession == null) {
      firstActiveSession = session;
    }

    for (const ticketId of matchedTicketIds) {
      const ticket = ticketsById.get(ticketId);
      if (ticket) {
        ticket.matchedPlayerCount = matchedTicketIds.length;
      }
    }
  }

  activeMatch = firstActiveSession;
}

function normalizePlayerId(playerId) {
  if (typeof playerId !== "string" || playerId.trim().length === 0) {
    return "anonymous";
  }
  return playerId.trim();
}

function normalizePosition(position) {
  return {
    x: normalizeNumber(position && position.x, 0),
    y: normalizeNumber(position && position.y, 0),
    z: normalizeNumber(position && position.z, 0)
  };
}

function normalizeNumber(value, fallback) {
  const n = Number(value);
  return Number.isFinite(n) ? n : fallback;
}

function normalizeInt64(value, fallback) {
  const n = Number.parseInt(value, 10);
  return Number.isFinite(n) ? n : fallback;
}

function enableCors(res) {
  res.setHeader("Access-Control-Allow-Origin", "*");
  res.setHeader("Access-Control-Allow-Methods", "GET,POST,OPTIONS");
  res.setHeader("Access-Control-Allow-Headers", "Content-Type");
}

function respondJson(res, statusCode, payload) {
  res.writeHead(statusCode, { "Content-Type": "application/json; charset=utf-8" });
  res.end(JSON.stringify(payload));
}

function readJsonBody(req) {
  return new Promise((resolve) => {
    let raw = "";
    req.on("data", (chunk) => {
      raw += chunk.toString("utf8");
      if (raw.length > 1024 * 1024) {
        raw = "";
      }
    });
    req.on("end", () => {
      if (!raw) {
        resolve({});
        return;
      }

      try {
        resolve(JSON.parse(raw));
      } catch {
        resolve({});
      }
    });
    req.on("error", () => resolve({}));
  });
}

function toInt(value, fallback) {
  const n = Number.parseInt(value, 10);
  return Number.isFinite(n) ? n : fallback;
}

function safeWsClose(socket, code, reason) {
  try {
    socket.close(code, reason);
  } catch {
    // ignored
  }
}

function isTicketSocketLive(ticketId) {
  const socket = wsClientsByTicketId.get(ticketId);
  return !!socket && socket.readyState === WebSocket.OPEN;
}

function dropTicketRealtimeConnection(ticketId, reason) {
  const socket = wsClientsByTicketId.get(ticketId);
  if (!socket) {
    return;
  }

  wsClientsByTicketId.delete(ticketId);
  wsMetaBySocket.delete(socket);
  safeWsClose(socket, 1000, reason || "connection closed");
}

function runMaintenanceSweep() {
  pruneQueuedTickets();
  pruneStaleTickets(Date.now());
  synchronizeActiveMatchCounts();
  tickBattleRoyaleMatches(Date.now());
  runMatchLifecycleManager();
}

function canRecycleActiveMatchForNewQueue(nowMs) {
  if (activeMatch == null || activeMatch.ticketIds.size === 0) {
    return true;
  }

  let hasMatchedTickets = false;
  let hasRecentOrLivePlayers = false;
  for (const ticketId of activeMatch.ticketIds) {
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Matched") {
      continue;
    }

    hasMatchedTickets = true;
    const liveSocket = isTicketSocketLive(ticketId);
    const hasRecentPresence = ticket.presence &&
      ticket.presence.lastSeenMs > 0 &&
      (nowMs - ticket.presence.lastSeenMs) <= PRESENCE_TIMEOUT_SECONDS * 1000;
    const inConnectGrace = ticket.matchedAtMs > 0 &&
      (nowMs - ticket.matchedAtMs) <= MATCH_CONNECT_GRACE_SECONDS * 1000;

    if (liveSocket || hasRecentPresence || inConnectGrace) {
      hasRecentOrLivePlayers = true;
      break;
    }
  }

  if (!hasMatchedTickets) {
    return true;
  }

  return !hasRecentOrLivePlayers;
}

function recycleActiveMatchIfNeeded() {
  if (activeMatch == null) {
    return;
  }

  for (const ticketId of activeMatch.ticketIds) {
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Matched") {
      continue;
    }

    ticket.status = "Disconnected";
    dropTicketRealtimeConnection(ticketId, "match recycled");
  }

  activeMatch.state = "Ended";
  activeMatch.endedAtMs = Date.now();
  activeMatch = null;
}

function pruneStaleTickets(nowMs) {
  for (const [ticketId, ticket] of ticketsById.entries()) {
    if (!ticket) {
      ticketsById.delete(ticketId);
      continue;
    }

    if (ticket.status === "Queued") {
      continue;
    }

    if (ticket.status === "Matched") {
      if (isTicketSocketLive(ticketId)) {
        continue;
      }

      const presenceRecent = ticket.presence &&
        ticket.presence.lastSeenMs > 0 &&
        (nowMs - ticket.presence.lastSeenMs) <= PRESENCE_TIMEOUT_SECONDS * 1000 * 2;
      if (presenceRecent) {
        continue;
      }
    }

    const terminalStatuses = new Set(["Disconnected", "Cancelled", "Left"]);
    if (!terminalStatuses.has(ticket.status) && ticket.status !== "Matched") {
      continue;
    }

    const anchorMs = Math.max(ticket.matchedAtMs || 0, ticket.queueEnterTimeMs || 0);
    if (anchorMs <= 0 || (nowMs - anchorMs) < STALE_TICKET_TTL_MS) {
      continue;
    }

    removeTicketFromMatchIndex(ticket);
    ticketsById.delete(ticketId);
  }
}

function pruneQueuedTickets() {
  if (queuedTicketIds.length === 0) {
    return;
  }

  for (let i = queuedTicketIds.length - 1; i >= 0; i--) {
    const ticketId = queuedTicketIds[i];
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Queued") {
      queuedTicketIds.splice(i, 1);
      continue;
    }

    expireTicketIfNeeded(ticket);
    if (ticket.status !== "Queued") {
      queuedTicketIds.splice(i, 1);
    }
  }
}

function cancelExistingQueuedTicketsForPlayer(playerId) {
  if (!playerId) {
    return;
  }

  for (let i = queuedTicketIds.length - 1; i >= 0; i--) {
    const ticketId = queuedTicketIds[i];
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Queued") {
      queuedTicketIds.splice(i, 1);
      continue;
    }

    if (ticket.playerId !== playerId) {
      continue;
    }

    ticket.status = "Cancelled";
    queuedTicketIds.splice(i, 1);
  }
}

function removeDisconnectedMatchedTicketsForPlayer(playerId) {
  if (!playerId) {
    return;
  }

  for (const ticket of ticketsById.values()) {
    if (!ticket || ticket.playerId !== playerId) {
      continue;
    }

    if (ticket.status !== "Matched" && ticket.status !== "Disconnected") {
      continue;
    }

    ticket.status = "Disconnected";
    removeTicketFromMatchIndex(ticket);
    if (ticket.matchId) {
      const session = matchesById.get(ticket.matchId);
      if (session) {
        session.ticketIds.delete(ticket.ticketId);
      }
    }
    dropTicketRealtimeConnection(ticket.ticketId, "stale player ticket replaced");
  }
}

function tryRestoreActiveMatchByTicket(ownerTicket) {
  if (!ownerTicket || !ownerTicket.matchId) {
    return;
  }

  const existingSession = matchesById.get(ownerTicket.matchId);
  if (existingSession && existingSession.state !== "Ended") {
    activeMatch = existingSession;
    return;
  }

  const ticketIds = [];
  for (const ticket of ticketsById.values()) {
    if (!ticket || ticket.status !== "Matched") {
      continue;
    }

    if (ticket.matchId === ownerTicket.matchId) {
      ticketIds.push(ticket.ticketId);
    }
  }

  if (ticketIds.length === 0) {
    return;
  }

  const nowMs = Date.now();
  activeMatch = {
    matchId: ownerTicket.matchId,
    createdAtMs: ownerTicket.matchedAtMs || nowMs,
    startedAtMs: ownerTicket.matchedAtMs || nowMs,
    endedAtMs: 0,
    lastActivityMs: nowMs,
    state: "Active",
    ticketIds: new Set(ticketIds)
  };
  matchesById.set(activeMatch.matchId, activeMatch);
  for (let i = 0; i < ticketIds.length; i++) {
    const ticket = ticketsById.get(ticketIds[i]);
    if (ticket) {
      addTicketToMatchIndex(ticket);
    }
  }
}

function tryRestoreAnyActiveMatch() {
  for (const session of matchesById.values()) {
    if (session && session.state !== "Ended" && session.ticketIds.size > 0) {
      activeMatch = session;
      return;
    }
  }

  for (const ticket of ticketsById.values()) {
    if (!ticket || ticket.status !== "Matched" || !ticket.matchId) {
      continue;
    }

    tryRestoreActiveMatchByTicket(ticket);
    if (activeMatch != null) {
      return;
    }
  }
}

function isBattleRoyaleOnPlane(ticket) {
  if (!ticket || !ticket.matchId) {
    return false;
  }

  const session = matchesById.get(ticket.matchId);
  const br = session ? ensureBattleRoyaleState(session) : null;
  if (!br || br.phase !== "plane") {
    return false;
  }

  return !br.jumpedTickets.has(ticket.ticketId);
}

function isBattleRoyaleParachuting(ticket) {
  if (!ticket || !ticket.matchId) {
    return false;
  }

  const session = matchesById.get(ticket.matchId);
  const br = session ? ensureBattleRoyaleState(session) : null;
  if (!br) {
    return false;
  }

  const ticketId = ticket.ticketId;
  return br.jumpedTickets.has(ticketId) && !br.landedTickets.has(ticketId);
}

function isBattleRoyaleRespawnBlocked(ticket) {
  if (!ticket || !ticket.matchId) {
    return false;
  }

  const session = matchesById.get(ticket.matchId);
  const br = session ? ensureBattleRoyaleState(session) : null;
  if (!br) {
    return false;
  }

  return br.phase === "playing" || br.phase === "ending";
}

function isBattleRoyaleCombatAllowed(ticket) {
  if (!ticket || !ticket.matchId) {
    return true;
  }

  const session = matchesById.get(ticket.matchId);
  const br = session ? ensureBattleRoyaleState(session) : null;
  if (!br) {
    return true;
  }

  return br.landedTickets.has(ticket.ticketId) || br.phase === "playing";
}

function getBattleRoyaleKillCountForTicket(matchId, ticketId) {
  if (!matchId || !ticketId) {
    return 0;
  }

  const session = matchesById.get(matchId);
  const br = session ? ensureBattleRoyaleState(session) : null;
  if (!br || !br.killCountByTicket) {
    return 0;
  }

  return Math.max(0, br.killCountByTicket.get(ticketId) || 0);
}

function recordBattleRoyaleKill(attackerTicket) {
  if (!attackerTicket || !attackerTicket.matchId) {
    return;
  }

  const session = matchesById.get(attackerTicket.matchId);
  const br = ensureBattleRoyaleState(session);
  if (!br || br.phase === "ending") {
    return;
  }

  if (!br.killCountByTicket) {
    br.killCountByTicket = new Map();
  }

  const ticketId = attackerTicket.ticketId;
  const nextCount = getBattleRoyaleKillCountForTicket(attackerTicket.matchId, ticketId) + 1;
  br.killCountByTicket.set(ticketId, nextCount);
  if (attackerTicket.presence) {
    attackerTicket.presence.killCount = nextCount;
  }
}

function maybeStartBattleRoyaleCountdown(session) {
  if (!session) {
    return;
  }

  const br = ensureBattleRoyaleState(session);
  if (!br || br.phase !== "lobby" || br.countdownEndsAtMs > 0) {
    return;
  }

  if (br.connectedTickets.size < MIN_PLAYERS_TO_MATCH) {
    return;
  }

  const nowMs = Date.now();
  br.phase = "countdown";
  br.countdownEndsAtMs = nowMs + (BR_LOBBY_COUNTDOWN_SECONDS * 1000);
  br.joinLocked = true;
  broadcastMatchState(session.matchId);
}

function assignBattleRoyalePlaneSpawns(session) {
  const br = ensureBattleRoyaleState(session);
  if (!br || !session) {
    return;
  }

  if (!br.planeSpawnIndexByTicket) {
    br.planeSpawnIndexByTicket = new Map();
  }

  br.planeSpawnIndexByTicket.clear();
  const liveTickets = Array.from(session.ticketIds)
    .filter((ticketId) => br.connectedTickets.has(ticketId))
    .sort();

  for (let i = 0; i < liveTickets.length; i++) {
    br.planeSpawnIndexByTicket.set(liveTickets[i], i % BR_PLANE_SPAWN_SLOTS);
  }
}

function computeBattleRoyalePlaneDurationSeconds(br) {
  const dx = Number(br.planeEndX) - Number(br.planeStartX);
  const dz = Number(br.planeEndZ) - Number(br.planeStartZ);
  const pathLength = Math.max(1, Math.hypot(dx, dz));
  const speed = Math.max(5, Number(br.planeSpeed) || BR_PLANE_SPEED);
  const travelSeconds = pathLength / speed;
  const minFlightSeconds = Math.max(12, BR_PLANE_DURATION_SECONDS * 0.85);
  return Math.max(minFlightSeconds, Math.min(BR_PLANE_DURATION_SECONDS, travelSeconds));
}

function startBattleRoyalePlane(session, nowMs) {
  const br = ensureBattleRoyaleState(session);
  if (!br) {
    return;
  }

  br.phase = "plane";
  br.planeStartedAtMs = nowMs;
  const planeDurationSeconds = computeBattleRoyalePlaneDurationSeconds(br);
  br.planeEndsAtMs = nowMs + (planeDurationSeconds * 1000);
  br.voluntaryJumpTickets = new Set();
  br.dropPositionsByTicket = new Map();
  assignBattleRoyalePlaneSpawns(session);
  broadcastMatchState(session.matchId);
}

function startBattleRoyalePlaying(session, nowMs) {
  const br = ensureBattleRoyaleState(session);
  if (!br) {
    return;
  }

  br.phase = "playing";
  br.playingStartedAtMs = nowMs;
  session.startedAtMs = nowMs;

  const zone = matchDamageZonesByMatchId.get(session.matchId);
  if (zone && zone.configured && zone.startedAtMs <= 0) {
    zone.startedAtMs = nowMs;
    zone.lastBroadcastMs = 0;
    broadcastZoneState(session.matchId);
  }

  broadcastMatchState(session.matchId);
}

function startBattleRoyaleEnding(session, nowMs, winnerTicketId) {
  const br = ensureBattleRoyaleState(session);
  if (!br) {
    return;
  }

  const nonWinnerTicketIds = [];
  for (const ticketId of session.ticketIds) {
    if (ticketId !== winnerTicketId) {
      nonWinnerTicketIds.push(ticketId);
    }
  }

  br.phase = "ending";
  br.endingStartedAtMs = nowMs;
  br.winnerTicketId = winnerTicketId || "";
  br.disconnectAtByTicket.clear();
  if (winnerTicketId) {
    br.disconnectAtByTicket.set(
      winnerTicketId,
      nowMs + (BR_WINNER_DISCONNECT_SECONDS * 1000)
    );
  }

  broadcastMatchState(session.matchId);

  for (const ticketId of nonWinnerTicketIds) {
    if (isTicketSocketLive(ticketId)) {
      forceBattleRoyaleDisconnect(ticketId, "eliminated");
    }
  }
}

function onBattleRoyalePlayerDied(ticket) {
  if (!ticket || !ticket.matchId) {
    return;
  }

  const session = matchesById.get(ticket.matchId);
  const br = ensureBattleRoyaleState(session);
  if (!br || (br.phase !== "playing" && br.phase !== "plane")) {
    return;
  }

  const ticketId = ticket.ticketId;
  if (br.eliminatedTickets.has(ticketId)) {
    return;
  }
  if (br.phase === "plane" && !br.landedTickets.has(ticketId)) {
    return;
  }
  br.aliveTickets.delete(ticketId);
  br.eliminatedTickets.add(ticketId);
  br.deathFallPendingTickets.add(ticketId);
  br.deathFallPendingSinceByTicket.set(ticketId, Date.now());
  br.disconnectAtByTicket.set(
    ticketId,
    Date.now() + (BR_DEATH_DISCONNECT_SECONDS * 1000)
  );
  checkBattleRoyaleWinner(session, Date.now());
  broadcastMatchState(session.matchId);
}

function handleWsDeathFallLanded(socket) {
  const meta = wsMetaBySocket.get(socket);
  if (!meta || !meta.ticketId) {
    return;
  }

  const ticket = ticketsById.get(meta.ticketId);
  if (!ticket || ticket.status !== "Matched" || !ticket.matchId) {
    return;
  }

  const session = matchesById.get(ticket.matchId);
  const br = ensureBattleRoyaleState(session);
  if (!br || !br.eliminatedTickets.has(ticket.ticketId)) {
    return;
  }

  br.deathFallPendingTickets.delete(ticket.ticketId);
  br.deathFallPendingSinceByTicket.delete(ticket.ticketId);
  checkBattleRoyaleWinner(session, Date.now());
  broadcastMatchState(session.matchId);
}

function checkBattleRoyaleWinner(session, nowMs) {
  const br = ensureBattleRoyaleState(session);
  if (!br || (br.phase !== "playing" && br.phase !== "plane")) {
    return;
  }

  if (br.phase === "plane" && br.landedTickets.size === 0) {
    return;
  }

  if (br.aliveTickets.size > 1) {
    br.pendingWinnerTicketId = "";
    return;
  }

  const winnerTicketId = br.aliveTickets.size === 1
    ? Array.from(br.aliveTickets)[0]
    : "";
  br.pendingWinnerTicketId = winnerTicketId;

  if (br.deathFallPendingTickets.size > 0) {
    return;
  }

  startBattleRoyaleEnding(session, nowMs, winnerTicketId);
}

function computeMapSquareEdgePoint(centerX, centerZ, dirX, dirZ, halfEdge) {
  const edge = Math.max(1, Number(halfEdge) || BR_DROP_MAX_DISTANCE_FROM_CENTER);
  const len = Math.hypot(dirX, dirZ);
  if (len < 1e-6) {
    return { x: centerX + edge, z: centerZ };
  }

  const ux = dirX / len;
  const uz = dirZ / len;
  let t = Infinity;
  if (ux > 1e-6) {
    t = Math.min(t, edge / ux);
  }
  if (ux < -1e-6) {
    t = Math.min(t, -edge / ux);
  }
  if (uz > 1e-6) {
    t = Math.min(t, edge / uz);
  }
  if (uz < -1e-6) {
    t = Math.min(t, -edge / uz);
  }
  if (!Number.isFinite(t) || t < 0) {
    t = edge;
  }

  return {
    x: centerX + (ux * t),
    z: centerZ + (uz * t)
  };
}

function clampPositionToMapSquare(x, z, centerX, centerZ, halfEdge) {
  const edge = Math.max(1, Number(halfEdge) || BR_DROP_MAX_DISTANCE_FROM_CENTER);
  return {
    x: Math.max(centerX - edge, Math.min(centerX + edge, x)),
    z: Math.max(centerZ - edge, Math.min(centerZ + edge, z))
  };
}

function updateBattleRoyalePlaneRoute(br, mapCenterX, mapCenterZ) {
  const routeHalfLength = Math.max(BR_DROP_MAX_DISTANCE_FROM_CENTER, BR_PLANE_HALF_LENGTH);
  if (!br.planePathInitialized) {
    br.planePathAngle = Math.random() * Math.PI * 2;
    br.planePathInitialized = true;
  }

  const pathDx = Math.cos(br.planePathAngle);
  const pathDz = Math.sin(br.planePathAngle);
  const start = computeMapSquareEdgePoint(mapCenterX, mapCenterZ, -pathDx, -pathDz, routeHalfLength);
  const end = computeMapSquareEdgePoint(mapCenterX, mapCenterZ, pathDx, pathDz, routeHalfLength);
  br.planeStartX = start.x;
  br.planeStartZ = start.z;
  br.planeEndX = end.x;
  br.planeEndZ = end.z;
  br.planeY = BR_PLANE_ALTITUDE;
  br.planeSpeed = BR_PLANE_SPEED;
}

function rollAutoDropPositionAtPlaneRouteEnd(br, ticketId) {
  const centerX = Number(br.mapCenterX) || 0;
  const centerZ = Number(br.mapCenterZ) || 0;
  const endX = Number(br.planeEndX) || 0;
  const endZ = Number(br.planeEndZ) || 0;
  const startX = Number(br.planeStartX) || 0;
  const startZ = Number(br.planeStartZ) || 0;
  const pathDx = endX - startX;
  const pathDz = endZ - startZ;
  const pathLength = Math.max(1, Math.hypot(pathDx, pathDz));
  const dirX = pathDx / pathLength;
  const dirZ = pathDz / pathLength;
  const perpX = -dirZ;
  const perpZ = dirX;

  const mapEdgeEnd = computeMapSquareEdgePoint(centerX, centerZ, dirX, dirZ, BR_DROP_MAX_DISTANCE_FROM_CENTER);
  const spawnIndex = br.planeSpawnIndexByTicket && ticketId
    ? (br.planeSpawnIndexByTicket.get(ticketId) ?? 0)
    : 0;
  const spawnCount = Math.max(1, BR_PLANE_SPAWN_SLOTS);
  const lateralStep = 3.25;
  const lateralOffset = (spawnIndex - ((spawnCount - 1) * 0.5)) * lateralStep;
  const alongJitter = (Math.random() - 0.5) * 2.5;
  const lateralJitter = (Math.random() - 0.5) * 1.0;

  const raw = {
    x: mapEdgeEnd.x + (perpX * (lateralOffset + lateralJitter)) + (dirX * alongJitter),
    z: mapEdgeEnd.z + (perpZ * (lateralOffset + lateralJitter)) + (dirZ * alongJitter)
  };
  return clampPositionToMapSquare(raw.x, raw.z, centerX, centerZ, BR_DROP_MAX_DISTANCE_FROM_CENTER);
}

function ensureAutoDropPosition(br, ticketId) {
  if (!br.dropPositionsByTicket) {
    br.dropPositionsByTicket = new Map();
  }

  if (!br.dropPositionsByTicket.has(ticketId)) {
    const drop = rollAutoDropPositionAtPlaneRouteEnd(br, ticketId);
    br.dropPositionsByTicket.set(ticketId, drop);
  }

  return br.dropPositionsByTicket.get(ticketId);
}

function hasPlaneReachedRouteEnd(br, nowMs) {
  return !!(br &&
    br.planeEndsAtMs > br.planeStartedAtMs &&
    nowMs >= br.planeEndsAtMs);
}

function hasPlaneCrossedMapExitEdge(br, planePosX, planePosZ) {
  if (!br) {
    return false;
  }

  const centerX = Number(br.mapCenterX) || 0;
  const centerZ = Number(br.mapCenterZ) || 0;
  const edge = BR_DROP_MAX_DISTANCE_FROM_CENTER;
  const relX = Number(planePosX) - centerX;
  const relZ = Number(planePosZ) - centerZ;

  if (Math.abs(relX) <= edge && Math.abs(relZ) <= edge) {
    return false;
  }

  const startX = Number(br.planeStartX) || 0;
  const startZ = Number(br.planeStartZ) || 0;
  const endX = Number(br.planeEndX) || 0;
  const endZ = Number(br.planeEndZ) || 0;
  const distToStart = Math.hypot(planePosX - startX, planePosZ - startZ);
  const distToEnd = Math.hypot(planePosX - endX, planePosZ - endZ);
  return distToEnd < distToStart;
}

function shouldForcePlaneJump(br, nowMs, ticketId, planePosX, planePosZ) {
  if (!br || !ticketId || br.phase !== "plane") {
    return false;
  }

  if (br.jumpedTickets.has(ticketId)) {
    return false;
  }

  if (br.voluntaryJumpTickets && br.voluntaryJumpTickets.has(ticketId)) {
    return false;
  }

  return hasPlaneCrossedMapExitEdge(br, planePosX, planePosZ);
}

function onBattleRoyalePlayerLanded(ticket) {
  if (!ticket || !ticket.matchId) {
    return;
  }

  const session = matchesById.get(ticket.matchId);
  const br = ensureBattleRoyaleState(session);
  if (!br || (br.phase !== "plane" && br.phase !== "playing")) {
    return;
  }

  if (!br.jumpedTickets.has(ticket.ticketId)) {
    br.jumpedTickets.add(ticket.ticketId);
  }

  br.landedTickets.add(ticket.ticketId);

  if (br.playingStartedAtMs <= 0) {
    br.playingStartedAtMs = Date.now();
    session.startedAtMs = br.playingStartedAtMs;
    const zone = matchDamageZonesByMatchId.get(session.matchId);
    if (zone && zone.configured && zone.startedAtMs <= 0) {
      zone.startedAtMs = br.playingStartedAtMs;
      zone.lastBroadcastMs = 0;
      broadcastZoneState(session.matchId);
    }
  }

  checkBattleRoyaleWinner(session, Date.now());
  broadcastMatchState(session.matchId);
}

function handleWsPlaneLanded(socket) {
  const meta = wsMetaBySocket.get(socket);
  if (!meta || !meta.ticketId) {
    return;
  }

  const ticket = ticketsById.get(meta.ticketId);
  if (!ticket || ticket.status !== "Matched" || !ticket.matchId) {
    return;
  }

  const input = ticket.inputState;
  const presence = ticket.presence;
  if (input && presence && Number.isFinite(input.clientX)) {
    presence.position = {
      x: normalizeNumber(input.clientX, presence.position?.x || 0),
      y: normalizeNumber(input.clientY, presence.position?.y || 0),
      z: normalizeNumber(input.clientZ, presence.position?.z || 0)
    };
    presence.isGrounded = true;
    presence.velocityX = 0;
    presence.velocityY = 0;
    presence.velocityZ = 0;
    presence.verticalVelocity = 0;
  }

  onBattleRoyalePlayerLanded(ticket);
  broadcastPlayerLand(ticket.matchId, ticket.ticketId);
}

function handleWsPlayerLand(socket) {
  const meta = wsMetaBySocket.get(socket);
  if (!meta || !meta.ticketId) {
    return;
  }

  const ticket = ticketsById.get(meta.ticketId);
  if (!ticket || ticket.status !== "Matched" || !ticket.matchId) {
    return;
  }

  broadcastPlayerLand(ticket.matchId, ticket.ticketId);
}

function broadcastPlayerLand(matchId, landedTicketId) {
  if (!matchId || !landedTicketId) {
    return;
  }

  const session = matchesById.get(matchId);
  if (!session) {
    return;
  }

  const payload = JSON.stringify({
    type: "player_land",
    ticketId: landedTicketId
  });

  for (const ticketId of session.ticketIds) {
    if (ticketId === landedTicketId) {
      continue;
    }

    const socket = wsClientsByTicketId.get(ticketId);
    if (!socket || socket.readyState !== WebSocket.OPEN) {
      continue;
    }

    try {
      socket.send(payload);
    } catch {
      // ignored
    }
  }
}

function handleWsPlaneJump(socket) {
  const meta = wsMetaBySocket.get(socket);
  if (!meta || !meta.ticketId) {
    return;
  }

  const ticket = ticketsById.get(meta.ticketId);
  if (!ticket || ticket.status !== "Matched" || !ticket.matchId) {
    return;
  }

  const session = matchesById.get(ticket.matchId);
  const br = ensureBattleRoyaleState(session);
  if (!br || br.phase !== "plane") {
    return;
  }

  br.jumpedTickets.add(ticket.ticketId);
  if (!br.voluntaryJumpTickets) {
    br.voluntaryJumpTickets = new Set();
  }
  br.voluntaryJumpTickets.add(ticket.ticketId);
  broadcastMatchState(session.matchId);
}

function buildMatchStatePayload(session, ticketId) {
  const br = ensureBattleRoyaleState(session);
  const nowMs = Date.now();
  if (!br) {
    return {
      type: "match_state",
      phase: "playing",
      joinLocked: false,
      countdownRemainingSeconds: 0,
      planeStartedAtMs: 0,
      planeEndsAtMs: 0,
      playingStartedAtMs: nowMs,
      endingStartedAtMs: 0,
      winnerTicketId: "",
      aliveCount: 0,
      connectedCount: 0,
      hasJumped: false,
      forceJump: false,
      localKillCount: 0,
      isLocalWinner: false,
      winnerDisconnectSeconds: 0,
      mapCenterX: 0,
      mapCenterZ: 0,
      planeStartX: 0,
      planeStartZ: 0,
      planeEndX: 0,
      planeEndZ: 0,
      planeY: BR_PLANE_ALTITUDE,
      planeSpeed: BR_PLANE_SPEED,
      planePosX: 0,
      planePosY: BR_PLANE_ALTITUDE,
      planePosZ: 0,
      planeSpawnIndex: -1,
      planeSpawnSlotCount: BR_PLANE_SPAWN_SLOTS,
      localTicketId: ticketId || ""
    };
  }

  let countdownRemainingSeconds = 0;
  if (br.phase === "countdown" && br.countdownEndsAtMs > nowMs) {
    countdownRemainingSeconds = Math.max(0, Math.ceil((br.countdownEndsAtMs - nowMs) / 1000));
  }

  const planeT = br.planeEndsAtMs > br.planeStartedAtMs
    ? Math.max(0, Math.min(1, (nowMs - br.planeStartedAtMs) / (br.planeEndsAtMs - br.planeStartedAtMs)))
    : 0;
  const planePosX = br.planeStartX + ((br.planeEndX - br.planeStartX) * planeT);
  const planePosZ = br.planeStartZ + ((br.planeEndZ - br.planeStartZ) * planeT);

  const forceJump = shouldForcePlaneJump(br, nowMs, ticketId, planePosX, planePosZ);
  const voluntaryJump = !!(ticketId && br.voluntaryJumpTickets && br.voluntaryJumpTickets.has(ticketId));
  let dropPosX = 0;
  let dropPosZ = 0;
  let useForcedDrop = false;
  if (ticketId && !voluntaryJump) {
    const missedJumpAfterPlane = br.phase === "playing" &&
      br.jumpedTickets.has(ticketId) &&
      !br.landedTickets.has(ticketId);
    if (missedJumpAfterPlane) {
      useForcedDrop = true;
      const drop = ensureAutoDropPosition(br, ticketId);
      dropPosX = drop.x;
      dropPosZ = drop.z;
    }
  }

  const hasLanded = ticketId ? br.landedTickets.has(ticketId) : false;
  const inCombat = br.phase !== "ending" && hasLanded;
  const isLocalWinner = !!(ticketId && br.winnerTicketId && ticketId === br.winnerTicketId && br.phase === "ending");
  let winnerDisconnectSeconds = 0;
  if (isLocalWinner && br.disconnectAtByTicket.has(ticketId)) {
    winnerDisconnectSeconds = Math.max(
      0,
      Math.ceil((br.disconnectAtByTicket.get(ticketId) - nowMs) / 1000)
    );
  }

  return {
    type: "match_state",
    phase: br.phase,
    joinLocked: !!br.joinLocked,
    countdownRemainingSeconds,
    planeStartedAtMs: br.planeStartedAtMs,
    planeEndsAtMs: br.planeEndsAtMs,
    playingStartedAtMs: br.playingStartedAtMs,
    endingStartedAtMs: br.endingStartedAtMs,
    winnerTicketId: br.winnerTicketId || "",
    aliveCount: br.aliveTickets.size,
    connectedCount: br.connectedTickets.size,
    hasJumped: ticketId ? br.jumpedTickets.has(ticketId) : false,
    hasLanded,
    inCombat,
    localKillCount: getBattleRoyaleKillCountForTicket(session.matchId, ticketId),
    isLocalWinner,
    winnerDisconnectSeconds,
    forceJump,
    useForcedDrop,
    dropPosX,
    dropPosZ,
    mapCenterX: br.mapCenterX,
    mapCenterZ: br.mapCenterZ,
    planeStartX: br.planeStartX,
    planeStartZ: br.planeStartZ,
    planeEndX: br.planeEndX,
    planeEndZ: br.planeEndZ,
    planeY: br.planeY,
    planeSpeed: br.planeSpeed,
    planePosX,
    planePosY: br.planeY,
    planePosZ,
    planeSpawnIndex: ticketId && br.planeSpawnIndexByTicket
      ? (br.planeSpawnIndexByTicket.get(ticketId) ?? 0)
      : 0,
    planeSpawnSlotCount: BR_PLANE_SPAWN_SLOTS,
    localTicketId: ticketId || ""
  };
}

function sendMatchStateToSocket(socket, session, ticketId) {
  if (!socket || socket.readyState !== WebSocket.OPEN || !session) {
    return;
  }

  try {
    socket.send(JSON.stringify(buildMatchStatePayload(session, ticketId)));
  } catch {
    // ignored
  }
}

function sendMatchStateToTicket(ticketId) {
  const ticket = ticketsById.get(ticketId);
  if (!ticket || !ticket.matchId) {
    return;
  }

  const session = matchesById.get(ticket.matchId);
  const socket = wsClientsByTicketId.get(ticketId);
  sendMatchStateToSocket(socket, session, ticketId);
}

function broadcastMatchState(matchId) {
  const session = matchesById.get(matchId);
  if (!session) {
    return;
  }

  const br = ensureBattleRoyaleState(session);
  if (br) {
    br.lastStateBroadcastMs = Date.now();
  }

  for (const ticketId of session.ticketIds) {
    const socket = wsClientsByTicketId.get(ticketId);
    sendMatchStateToSocket(socket, session, ticketId);
  }
}

function forceBattleRoyaleDisconnect(ticketId, reason) {
  const socket = wsClientsByTicketId.get(ticketId);
  if (socket && socket.readyState === WebSocket.OPEN) {
    try {
      socket.send(JSON.stringify({
        type: "match_disconnect",
        ticketId,
        reason: typeof reason === "string" ? reason : ""
      }));
    } catch {
      // ignored
    }
  }

  const ticket = ticketsById.get(ticketId);
  if (ticket) {
    ticket.status = "Disconnected";
    removeTicketFromMatchIndex(ticket);
    const session = ticket.matchId ? matchesById.get(ticket.matchId) : null;
    const br = ensureBattleRoyaleState(session);
    if (br) {
      br.connectedTickets.delete(ticketId);
      br.aliveTickets.delete(ticketId);
      br.jumpedTickets.delete(ticketId);
      br.landedTickets.delete(ticketId);
      if (br.dropPositionsByTicket) {
        br.dropPositionsByTicket.delete(ticketId);
      }
      br.disconnectAtByTicket.delete(ticketId);
      if (br.deathFallPendingTickets) {
        br.deathFallPendingTickets.delete(ticketId);
      }
      if (br.deathFallPendingSinceByTicket) {
        br.deathFallPendingSinceByTicket.delete(ticketId);
      }
    }
    if (session) {
      session.ticketIds.delete(ticketId);
      if (session.ticketIds.size === 0) {
        session.state = "Ended";
        session.endedAtMs = Date.now();
        if (activeMatch && activeMatch.matchId === session.matchId) {
          activeMatch = null;
        }
        matchDamageZonesByMatchId.delete(session.matchId);
        matchedTicketsByMatchId.delete(session.matchId);
      }
    }
  }

  dropTicketRealtimeConnection(ticketId, reason || "match disconnect");
}

function syncBattleRoyalePresence(session, nowMs) {
  const br = ensureBattleRoyaleState(session);
  if (!br) {
    return;
  }

  for (const ticketId of Array.from(br.connectedTickets)) {
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Matched" || !isTicketSocketLive(ticketId)) {
      br.connectedTickets.delete(ticketId);
    }
  }

  if (br.phase !== "playing" && br.phase !== "plane" && br.phase !== "ending") {
    return;
  }

  let changed = false;
  for (const ticketId of Array.from(br.aliveTickets)) {
    const ticket = ticketsById.get(ticketId);
    if (!ticket || ticket.status !== "Matched" || !isTicketSocketLive(ticketId)) {
      br.aliveTickets.delete(ticketId);
      br.eliminatedTickets.add(ticketId);
      changed = true;
    }
  }

  if (changed && (br.phase === "playing" || br.phase === "plane")) {
    checkBattleRoyaleWinner(session, nowMs);
  }
}

function handleBattleRoyalePlayerLeft(ticketId) {
  const ticket = ticketsById.get(ticketId);
  if (!ticket || !ticket.matchId) {
    return;
  }

  const session = matchesById.get(ticket.matchId);
  const br = ensureBattleRoyaleState(session);
  if (!br) {
    return;
  }

  br.connectedTickets.delete(ticketId);
  const wasAlive = br.aliveTickets.delete(ticketId);
  br.jumpedTickets.delete(ticketId);
  br.landedTickets.delete(ticketId);
  if (br.dropPositionsByTicket) {
    br.dropPositionsByTicket.delete(ticketId);
  }
  br.disconnectAtByTicket.delete(ticketId);
  if (br.deathFallPendingTickets) {
    br.deathFallPendingTickets.delete(ticketId);
  }
  if (br.deathFallPendingSinceByTicket) {
    br.deathFallPendingSinceByTicket.delete(ticketId);
  }

  if (wasAlive && (br.phase === "playing" || br.phase === "plane")) {
    checkBattleRoyaleWinner(session, Date.now());
    broadcastMatchState(session.matchId);
  }
}

function tickBattleRoyaleMatches(nowMs) {
  for (const session of matchesById.values()) {
    if (!session || session.state === "Ended") {
      continue;
    }

    const br = ensureBattleRoyaleState(session);
    if (!br) {
      continue;
    }

    syncBattleRoyalePresence(session, nowMs);

    if (br.deathFallPendingSinceByTicket && br.deathFallPendingSinceByTicket.size > 0) {
      const timeoutMs = BR_DEATH_FALL_LAND_TIMEOUT_SECONDS * 1000;
      for (const [ticketId, pendingSinceMs] of br.deathFallPendingSinceByTicket.entries()) {
        if (nowMs - pendingSinceMs >= timeoutMs) {
          br.deathFallPendingTickets.delete(ticketId);
          br.deathFallPendingSinceByTicket.delete(ticketId);
        }
      }

      if (br.aliveTickets.size <= 1 && br.deathFallPendingTickets.size === 0) {
        checkBattleRoyaleWinner(session, nowMs);
      }
    }

    if (br.phase === "countdown" && br.countdownEndsAtMs > 0 && nowMs >= br.countdownEndsAtMs) {
      startBattleRoyalePlane(session, nowMs);
    }

    if (br.phase === "plane" && hasPlaneReachedRouteEnd(br, nowMs)) {
      for (const ticketId of br.connectedTickets) {
        if (br.voluntaryJumpTickets && br.voluntaryJumpTickets.has(ticketId)) {
          continue;
        }
        if (!br.jumpedTickets.has(ticketId)) {
          br.jumpedTickets.add(ticketId);
        }
      }
      startBattleRoyalePlaying(session, nowMs);
    }

    const pendingDisconnects = [];
    for (const [ticketId, disconnectAtMs] of br.disconnectAtByTicket.entries()) {
      if (nowMs >= disconnectAtMs) {
        pendingDisconnects.push(ticketId);
      }
    }

    for (const ticketId of pendingDisconnects) {
      const reason = br.winnerTicketId && ticketId === br.winnerTicketId
        ? "winner"
        : "eliminated";
      forceBattleRoyaleDisconnect(ticketId, reason);
    }

    if (br.phase === "ending" && session.ticketIds.size === 0 && session.state !== "Ended") {
      session.state = "Ended";
      session.endedAtMs = nowMs;
      if (activeMatch && activeMatch.matchId === session.matchId) {
        activeMatch = null;
      }
      matchDamageZonesByMatchId.delete(session.matchId);
      matchedTicketsByMatchId.delete(session.matchId);
    }

    if (nowMs - (br.lastStateBroadcastMs || 0) >= BR_MATCH_STATE_BROADCAST_MS) {
      broadcastMatchState(session.matchId);
    }
  }
}

function runMatchLifecycleManager() {
  const nowMs = Date.now();
  const endedTtlMs = Math.max(60000, MATCH_TIMEOUT_SECONDS * 1000 * 10);
  for (const [matchId, session] of matchesById.entries()) {
    const activeTickets = Array.from(session.ticketIds).filter((ticketId) => {
      const ticket = ticketsById.get(ticketId);
      return !!ticket && ticket.status === "Matched";
    });

    session.ticketIds = new Set(activeTickets);
    session.lastActivityMs = nowMs;

    if (activeTickets.length === 0 && session.state !== "Ended") {
      session.state = "Ended";
      session.endedAtMs = nowMs;
    } else if (activeTickets.length > 0 && session.state === "Open") {
      session.state = "Active";
    }

    if (session.state === "Ended" && session.endedAtMs > 0 && (nowMs - session.endedAtMs) > endedTtlMs) {
      matchesById.delete(matchId);
    }
  }
}

function ensureTicketTelemetry(ticket) {
  if (!ticket.telemetry) {
    ticket.telemetry = {
      wsOpenCount: 0,
      wsCloseCount: 0,
      reconnectCount: 0,
      poseReceived: 0,
      poseMissing: 0,
      poseOutOfOrder: 0,
      lastPoseSeq: -1,
      lastWsOpenMs: 0,
      lastWsCloseMs: 0
    };
  }
  return ticket.telemetry;
}

function touchMatchSession(matchId) {
  if (!matchId) {
    return;
  }
  const session = matchesById.get(matchId);
  if (session) {
    session.lastActivityMs = Date.now();
  }
}

function toSerializableSession(session) {
  return {
    matchId: session.matchId,
    state: session.state,
    createdAtMs: session.createdAtMs,
    startedAtMs: session.startedAtMs || session.createdAtMs,
    endedAtMs: session.endedAtMs || 0,
    lastActivityMs: session.lastActivityMs || 0,
    playerCount: session.ticketIds ? session.ticketIds.size : 0,
    ticketIds: session.ticketIds ? Array.from(session.ticketIds) : []
  };
}

function buildMatchTelemetryPlayers(matchId) {
  const result = [];
  for (const ticket of ticketsById.values()) {
    if (!ticket || ticket.matchId !== matchId) {
      continue;
    }

    const telemetry = ensureTicketTelemetry(ticket);
    const expected = telemetry.poseReceived + telemetry.poseMissing;
    const packetLossPercent = expected > 0 ? (telemetry.poseMissing / expected) * 100 : 0;
    result.push({
      ticketId: ticket.ticketId,
      playerId: ticket.playerId,
      status: ticket.status,
      wsOpenCount: telemetry.wsOpenCount,
      wsCloseCount: telemetry.wsCloseCount,
      reconnectCount: telemetry.reconnectCount,
      poseReceived: telemetry.poseReceived,
      poseMissing: telemetry.poseMissing,
      poseOutOfOrder: telemetry.poseOutOfOrder,
      packetLossPercent: Number(packetLossPercent.toFixed(2)),
      lastWsOpenMs: telemetry.lastWsOpenMs,
      lastWsCloseMs: telemetry.lastWsCloseMs
    });
  }
  return result;
}
