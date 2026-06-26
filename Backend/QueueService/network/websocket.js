"use strict";

const WebSocket = require("ws");
const { WebSocketServer } = WebSocket;
const log = require("../lib/logger");
const config = require("../config");
const { PlayerEventType } = require("../workers/protocol");
const { decodeBinaryPose, isBinaryPose } = require("./poseBinary");

function createRealtimeServer(router) {
  const wss = new WebSocketServer({ port: config.realtimeWsPort });
  const socketsByTicket = new Map();
  const ticketBySocket = new Map();

  function resolveTicketId(socket, message) {
    const fromMessage = message && message.ticketId ? String(message.ticketId) : "";
    if (fromMessage) {
      return fromMessage;
    }
    return ticketBySocket.get(socket) || "";
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
        router.routePlayerEvent(ticketId, PlayerEventType.POSE, message);
        break;
      case "shot":
        router.routePlayerEvent(ticketId, PlayerEventType.SHOT, message);
        break;
      case "hit":
        router.routePlayerEvent(ticketId, PlayerEventType.HIT, message);
        break;
      case "weapon_pick":
      case "duel_weapon_pick":
        router.routePlayerEvent(ticketId, PlayerEventType.WEAPON_PICK, message);
        break;
      case "ping":
        router.routePlayerEvent(ticketId, PlayerEventType.PING, message);
        break;
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

  return {
    wss,
    sendJson(ticketId, payload) {
      const socket = socketsByTicket.get(ticketId);
      if (!socket || socket.readyState !== WebSocket.OPEN) {
        return false;
      }
      socket.send(JSON.stringify(payload));
      return true;
    },
    sendBinary(ticketId, buffer) {
      const socket = socketsByTicket.get(ticketId);
      if (!socket || socket.readyState !== WebSocket.OPEN) {
        return false;
      }
      socket.send(buffer);
      return true;
    },
    broadcastMatch(ticketIds, sendFn) {
      for (const ticketId of ticketIds) {
        sendFn(ticketId);
      }
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
