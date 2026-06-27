"use strict";

const WebSocket = require("ws");
const { WebSocketServer } = WebSocket;
const log = require("../lib/logger");
const config = require("../config");
const { PlayerEventType } = require("../workers/protocol");
const { decodeBinaryPose, isBinaryPose } = require("./poseBinary");
const { PerTicketRateLimiter } = require("../lib/rateLimiter");
const { SEND_PRIORITY, WsOutboundGate } = require("../lib/wsOutbound");

function createRealtimeServer(router) {
  const wss = new WebSocketServer({
    port: config.realtimeWsPort,
    perMessageDeflate: {
      zlibDeflateOptions: { level: 5 },
      threshold: 256,
    },
  });

  const socketsByTicket = new Map();
  const ticketBySocket = new Map();
  const outbound = new WsOutboundGate({
    softLimitBytes: config.wsMaxBufferedBytes,
    hardLimitBytes: config.wsMaxBufferedHardBytes,
  });

  const poseLimiter = new PerTicketRateLimiter(config.maxPoseMessagesPerSecond);
  const pingLimiter = new PerTicketRateLimiter(config.maxPingMessagesPerSecond);
  const shotLimiter = new PerTicketRateLimiter(config.maxShotMessagesPerSecond);
  const hitLimiter = new PerTicketRateLimiter(config.maxHitMessagesPerSecond);

  setInterval(() => {
    poseLimiter.prune();
    pingLimiter.prune();
    shotLimiter.prune();
    hitLimiter.prune();
  }, 60000).unref();

  function resolveTicketId(socket, message) {
    const fromMessage = message && message.ticketId ? String(message.ticketId) : "";
    if (fromMessage) {
      return fromMessage;
    }
    return ticketBySocket.get(socket) || "";
  }

  function sendToSocket(socket, payload, priority, dropKind) {
    return outbound.send(socket, payload, priority, () => {
      if (dropKind === "snapshot") {
        outbound.droppedSnapshots += 1;
      } else if (dropKind === "match_state") {
        outbound.droppedMatchStates += 1;
      } else {
        outbound.droppedLowPriority += 1;
      }
    });
  }

  function routeMessage(socket, ticketId, type, message) {
    if (!ticketId) {
      if (config.debugRealtime) {
        log.warn("ws", `drop ${type || "message"}: no ticket on socket`);
      }
      return;
    }

    switch (type) {
      case "pose":
        if (!poseLimiter.allow(ticketId)) {
          return;
        }
        router.routePlayerEvent(ticketId, PlayerEventType.POSE, message);
        break;
      case "shot":
        if (!shotLimiter.allow(ticketId)) {
          return;
        }
        router.routePlayerEvent(ticketId, PlayerEventType.SHOT, message);
        break;
      case "hit":
        if (!hitLimiter.allow(ticketId)) {
          return;
        }
        router.routePlayerEvent(ticketId, PlayerEventType.HIT, message);
        break;
      case "weapon_pick":
      case "duel_weapon_pick":
        router.routePlayerEvent(ticketId, PlayerEventType.WEAPON_PICK, message);
        break;
      case "ping":
        if (!pingLimiter.allow(ticketId)) {
          return;
        }
        if (socket.readyState === WebSocket.OPEN) {
          const clientTimeMs = message && message.clientTimeMs;
          sendToSocket(
            socket,
            `{"type":"pong","clientTimeMs":${Number(clientTimeMs) || 0}}`,
            SEND_PRIORITY.CRITICAL
          );
        }
        return;
      default:
        if (config.debugRealtime) {
          log.warn("ws", `unknown message type=${type}`);
        }
        break;
    }
  }

  wss.on("connection", (socket) => {
    ticketBySocket.set(socket, "");

    socket.on("message", (raw) => {
      const buffer = Buffer.isBuffer(raw) ? raw : Buffer.from(raw);

      if (isBinaryPose(buffer)) {
        const poseMessage = decodeBinaryPose(buffer);
        const ticketId = resolveTicketId(socket, poseMessage);
        if (poseMessage) {
          routeMessage(socket, ticketId, "pose", poseMessage);
        }
        return;
      }

      let message;
      try {
        message = JSON.parse(buffer.toString("utf8"));
      } catch {
        return;
      }

      const type = String(message.type || "");
      const ticketId = resolveTicketId(socket, message);

      if (type === "join") {
        if (!ticketId) {
          socket.close(1008, "missing ticket");
          return;
        }
        const bound = router.bindSocket(ticketId, socket);
        if (!bound) {
          if (config.debugRealtime) {
            log.warn("ws", `join rejected ticket=${ticketId.slice(0, 8)}`);
          }
          socket.close(1008, "invalid ticket");
          return;
        }
        socketsByTicket.set(ticketId, socket);
        ticketBySocket.set(socket, ticketId);
        router.routePlayerEvent(ticketId, PlayerEventType.JOIN, message);
        return;
      }

      routeMessage(socket, ticketId, type, message);
    });

    socket.on("close", () => {
      const ticketId = ticketBySocket.get(socket) || "";
      ticketBySocket.delete(socket);
      if (ticketId) {
        socketsByTicket.delete(ticketId);
        router.onSocketClose(ticketId);
        return;
      }
      for (const [id, s] of socketsByTicket.entries()) {
        if (s === socket) {
          socketsByTicket.delete(id);
          router.onSocketClose(id);
          break;
        }
      }
    });

    socket.on("error", () => {
      socket.close();
    });
  });

  log.info("ws", `listening ws://0.0.0.0:${config.realtimeWsPort}`);

  function getSocket(ticketId) {
    return socketsByTicket.get(ticketId) || null;
  }

  return {
    wss,
    stats() {
      return outbound.snapshot();
    },
    sendJson(ticketId, payload, priority = SEND_PRIORITY.NORMAL) {
      const socket = getSocket(ticketId);
      if (!socket) {
        return false;
      }
      if (typeof payload === "string") {
        return sendToSocket(socket, payload, priority);
      }
      return sendToSocket(socket, JSON.stringify(payload), priority);
    },
    sendRaw(ticketId, payload, priority, dropKind = "") {
      const socket = getSocket(ticketId);
      if (!socket) {
        return false;
      }
      const resolvedPriority = priority === undefined ? SEND_PRIORITY.NORMAL : priority;
      return sendToSocket(socket, payload, resolvedPriority, dropKind);
    },
    sendBinary(ticketId, buffer, priority = SEND_PRIORITY.NORMAL, dropKind = "snapshot") {
      const socket = getSocket(ticketId);
      if (!socket) {
        return false;
      }
      return sendToSocket(socket, buffer, priority, dropKind);
    },
    closeAll() {
      ticketBySocket.clear();
      for (const socket of socketsByTicket.values()) {
        try {
          socket.close(1001, "server shutdown");
        } catch {
          // ignored
        }
      }
      socketsByTicket.clear();
      wss.close();
    },
  };
}

module.exports = { createRealtimeServer };
