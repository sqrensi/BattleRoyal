const http = require("http");
const { URL } = require("url");
const crypto = require("crypto");
const WebSocket = require("ws");
const { WebSocketServer } = WebSocket;

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
const SERVER_TICK_RATE = Math.max(10, toInt(process.env.SERVER_TICK_RATE, 128));
const REALTIME_WS_PORT = toInt(process.env.REALTIME_WS_PORT, 5051);
const DEBUG_REALTIME = (process.env.DEBUG_REALTIME || "1") !== "0";
const DEBUG_MOVEMENT = (process.env.DEBUG_MOVEMENT || "1") !== "0";
const POSE_HISTORY_KEEP_MS = Math.max(200, toInt(process.env.POSE_HISTORY_KEEP_MS, 500));
const SNAPSHOT_HISTORY_SAMPLES = Math.max(4, toInt(process.env.SNAPSHOT_HISTORY_SAMPLES, 16));
const USE_BINARY_SNAPSHOTS = (process.env.USE_BINARY_SNAPSHOTS || "1") !== "0";
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
const droppedWeaponSeqByMatchId = new Map();
const WEAPON_SLOT_EMPTY = 255;
const WEAPON_MAX_SLOTS = 2;
const PICKUP_MAX_DISTANCE = Number.isFinite(Number(process.env.PICKUP_MAX_DISTANCE))
  ? Math.max(1, Number(process.env.PICKUP_MAX_DISTANCE))
  : 2.75;
const MEDKIT_USE_DURATION_SECONDS = Math.max(0.5, Number(process.env.MEDKIT_USE_DURATION_SECONDS) || 8);
const MEDKIT_HEAL_AMOUNT = Math.max(1, Number(process.env.MEDKIT_HEAL_AMOUNT) || 70);
const MEDKIT_MAX_COUNT = Math.max(1, Math.min(99, Number(process.env.MEDKIT_MAX_COUNT) || 8));
const MEDKIT_STARTING_COUNT = Math.max(0, Math.min(MEDKIT_MAX_COUNT, Number(process.env.MEDKIT_STARTING_COUNT) || 0));

setInterval(() => {
  currentServerTick += 1;
  tickAllPlayerMovement();
  tickMedkitUses();
  tickPickupRespawns();
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
      activeSessionCount: Array.from(matchesById.values()).filter((m) => m.state !== "Ended").length
    });
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
    const ticket = createQueuedTicket(playerId);
    if (DEBUG_REALTIME) {
      console.log(`[http][enqueue] player=${playerId} ticket=${ticket.ticketId}`);
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

      if (message.type === "pickup") {
        handleWsPickup(socket, message);
        return;
      }

      if (message.type === "weapon_drop") {
        handleWsWeaponDrop(socket, message);
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

function createQueuedTicket(playerId) {
  cancelExistingQueuedTicketsForPlayer(playerId);
  removeDisconnectedMatchedTicketsForPlayer(playerId);

  const ticketId = crypto.randomUUID();
  const ticket = {
    ticketId,
    playerId,
    status: "Queued",
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
    serverPort: 0
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

  const requiredPlayers = Math.max(MIN_PLAYERS_TO_MATCH, TARGET_PLAYERS_PER_MATCH);
  if (queuedTicketIds.length < requiredPlayers) {
    return;
  }

  const oldestTicket = ticketsById.get(queuedTicketIds[0]);
  if (!oldestTicket) {
    return;
  }
  const oldestWaitMs = nowMs - oldestTicket.queueEnterTimeMs;
  const batchWindowElapsed = oldestWaitMs >= MATCH_BATCH_WINDOW_SECONDS * 1000;
  const shouldMatchByTimeout = oldestWaitMs >= MATCH_TIMEOUT_SECONDS * 1000;
  if (!batchWindowElapsed && !shouldMatchByTimeout) {
    return;
  }

  const matchSize = Math.min(TARGET_PLAYERS_PER_MATCH, queuedTicketIds.length);
  const matchedTickets = [];
  for (let i = 0; i < matchSize && queuedTicketIds.length > 0; i++) {
    const ticketId = queuedTicketIds.shift();
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

function getOrCreateActiveMatch() {
  const nowMs = Date.now();
  const session = {
    matchId: crypto.randomUUID(),
    createdAtMs: nowMs,
    startedAtMs: nowMs,
    endedAtMs: 0,
    lastActivityMs: nowMs,
    state: "Open",
    ticketIds: new Set()
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

    const joinableSessions = getJoinableSessions();
    if (joinableSessions.length === 0) {
      continue;
    }

    const selected = joinableSessions[Math.floor(Math.random() * joinableSessions.length)];
    queuedTicketIds.splice(i, 1);
    matchTicketToSession(ticket, selected, nowMs);
  }
}

function getJoinableSessions() {
  const sessions = [];
  for (const session of matchesById.values()) {
    if (!session || session.state === "Ended") {
      continue;
    }

    let matchedCount = 0;
    for (const ticketId of session.ticketIds) {
      const ticket = ticketsById.get(ticketId);
      if (ticket && ticket.status === "Matched") {
        matchedCount++;
      }
    }

    if (matchedCount >= 1 && matchedCount < TARGET_PLAYERS_PER_MATCH) {
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
  ticket.serverAddress = MATCH_SERVER_ADDRESS;
  ticket.serverPort = MATCH_SERVER_PORT;
  session.ticketIds.add(ticket.ticketId);
  session.lastActivityMs = nowMs;
  if (session.state === "Open") {
    session.state = "Active";
  }

  if (ticket.presence == null) {
    ticket.presence = createDefaultPresence(0, nowMs);
  }
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
  return 0;
}

function isWeaponSlotOccupied(kind) {
  return kind !== WEAPON_SLOT_EMPTY && kind >= 0 && kind <= 1;
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

function createDroppedWeaponSpawn(matchId, ticketId, position, yaw, itemId, magAmmo = -1) {
  const state = ensureMatchPickups(matchId);
  if (!state || !position) {
    return "";
  }

  const seq = (droppedWeaponSeqByMatchId.get(matchId) || 0) + 1;
  droppedWeaponSeqByMatchId.set(matchId, seq);
  const spawnId = `drop_${ticketId}_${seq}`;
  const rad = (Number.isFinite(yaw) ? yaw : 0) * Math.PI / 180;
  const offset = 1.2;
  const x = position.x + Math.sin(rad) * offset;
  const z = position.z + Math.cos(rad) * offset;
  const normalizedMagAmmo = Number.isFinite(magAmmo) ? Math.max(-1, Math.min(999, magAmmo)) : -1;
  state.spawns.set(spawnId, {
    spawnId,
    pickupKind: "weapon",
    itemId: itemId || "",
    weaponId: itemId || "",
    amount: 1,
    magAmmo: normalizedMagAmmo,
    x,
    y: position.y,
    z,
    available: true,
    respawnDelaySeconds: 0,
    respawnAtMs: 0,
  });
  state.version += 1;
  return spawnId;
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
  const weaponKind = Math.max(0, Math.min(1, normalizeInt64(message.weaponKind, prevPresence.weaponKind || 0)));
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
    presence.position = { x: position.x, y: position.y, z: position.z };
    presence.velocityX = 0;
    presence.velocityY = 0;
    presence.velocityZ = 0;
    presence.verticalVelocity = 0;
    const maxHealth = Number.isFinite(presence.maxHealth) ? presence.maxHealth : 100;
    presence.health = maxHealth;
    positionBranch = "respawn";
  } else if (isDead) {
    presence.position = { x: position.x, y: position.y, z: position.z };
    presence.velocityX = 0;
    presence.velocityY = 0;
    presence.velocityZ = 0;
    presence.verticalVelocity = 0;
    positionBranch = "dead";
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
  presence.wallAvoidBlend = wallAvoidBlend;
  presence.isDead = isDead;
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
    weaponPickupSeq: 0,
    medkitCount: MEDKIT_STARTING_COUNT,
    isUsingMedkit: false,
    medkitUseEndsAtMs: 0,
    medkitSeq: 0,
    health: 100,
    maxHealth: 100,
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
      if (existingSpawn.available && itemId) {
        existingSpawn.pickupKind = pickupKind;
        existingSpawn.itemId = itemId;
        existingSpawn.weaponId = itemId;
        existingSpawn.amount = Math.max(1, normalizeInt64(entry.amount, 1));
        existingSpawn.x = normalizeNumber(entry.x, existingSpawn.x);
        existingSpawn.y = normalizeNumber(entry.y, existingSpawn.y);
        existingSpawn.z = normalizeNumber(entry.z, existingSpawn.z);
        existingSpawn.respawnDelaySeconds = Math.max(0, normalizeNumber(
          entry.respawnDelaySeconds,
          existingSpawn.respawnDelaySeconds || 0
        ));
        added += 1;
      }
      continue;
    }

    state.spawns.set(spawnId, {
      spawnId,
      pickupKind,
      itemId,
      weaponId: itemId,
      amount: Math.max(1, normalizeInt64(entry.amount, 1)),
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
  }

  sendPickupStateToSocket(socket, ticket.matchId);
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
  if (pickupKind === "weapon") {
    let targetSlot = findFirstEmptyWeaponSlot(presence);
    if (targetSlot < 0) {
      targetSlot = resolveDropSlotIndex(presence, -1);
      const dropItemId = getWeaponSlotItemId(presence, targetSlot);
      if (targetSlot >= 0 && dropItemId) {
        droppedSpawnId = createDroppedWeaponSpawn(
          ticket.matchId,
          ticket.ticketId,
          pos,
          presence.yaw || 0,
          dropItemId,
          getWeaponSlotMagAmmo(presence, targetSlot)
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
    pickedMagAmmo = Number.isFinite(spawn.magAmmo) ? Math.max(-1, Math.min(999, spawn.magAmmo)) : -1;
    setWeaponSlotMagAmmo(presence, targetSlot, pickedMagAmmo);
    weaponPickupSeq += 1;
    presence.weaponPickupSeq = weaponPickupSeq;
    syncWeaponPresenceFlags(presence);
  } else if (pickupKind === "medkit") {
    presence.medkitCount = Math.min(
      MEDKIT_MAX_COUNT,
      Math.max(0, normalizeInt64(presence.medkitCount, 0)) + amount
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
    droppedSpawnId,
    magAmmo: pickedMagAmmo,
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
      droppedSpawnId: details && typeof details.droppedSpawnId === "string" ? details.droppedSpawnId : "",
      weaponSlot0Kind: details && Number.isFinite(details.weaponSlot0Kind) ? details.weaponSlot0Kind : WEAPON_SLOT_EMPTY,
      weaponSlot1Kind: details && Number.isFinite(details.weaponSlot1Kind) ? details.weaponSlot1Kind : WEAPON_SLOT_EMPTY,
      weaponSlot0ItemId: details && typeof details.weaponSlot0ItemId === "string" ? details.weaponSlot0ItemId : "",
      weaponSlot1ItemId: details && typeof details.weaponSlot1ItemId === "string" ? details.weaponSlot1ItemId : "",
      activeWeaponSlot: details && Number.isFinite(details.activeWeaponSlot) ? details.activeWeaponSlot : WEAPON_SLOT_EMPTY,
      bothHolstered: !!(details && details.bothHolstered),
      magAmmo: details && Number.isFinite(details.magAmmo) ? details.magAmmo : -1,
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
    }));
  } catch {
    // ignored
  }
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

  const slotIndex = resolveDropSlotIndex(presence, message && message.slotIndex);
  if (slotIndex < 0 || !isWeaponSlotOccupied(getWeaponSlotKind(presence, slotIndex))) {
    sendWeaponDropResultToSocket(socket, false, "empty_slot");
    return;
  }

  const itemId = getWeaponSlotItemId(presence, slotIndex);
  const slotMagAmmo = getWeaponSlotMagAmmo(presence, slotIndex);
  const magAmmo = Math.max(-1, Math.min(999, normalizeInt64(message && message.magAmmo, slotMagAmmo)));
  const droppedSpawnId = createDroppedWeaponSpawn(
    ticket.matchId,
    ticket.ticketId,
    presence.position,
    presence.yaw || 0,
    itemId,
    magAmmo
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
    magAmmo,
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
        available: !!spawn.available
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
        available: true
      });
    }

    if (changed) {
      state.version += 1;
      broadcastPickupState(matchId);
    }
  }
}

function tickAllPlayerMovement() {
  for (const ticket of ticketsById.values()) {
    if (!ticket || ticket.status !== "Matched" || !ticket.presence || !ticket.presence.hasPose) {
      continue;
    }

    tickPlayerMovement(ticket);
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
  const dtSec = dtTicks / SERVER_TICK_RATE;
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
  targetPresence.health = Math.max(0, currentHealth - damage);
  if (targetPresence.health <= 0.001) {
    targetPresence.isDead = true;
    targetPresence.deathSeq = Math.max(0, normalizeInt64(targetPresence.deathSeq, 0)) + 1;
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
    header.writeUInt8(10, 4);
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
      meta.writeUInt8(Math.max(0, Math.min(1, player.weaponKind || 0)), 102);
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
  for (const ticket of ticketsById.values()) {
    if (!ticket || ticket.status !== "Matched" || ticket.matchId !== matchId || !ticket.presence) {
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
  synchronizeActiveMatchCounts();
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
