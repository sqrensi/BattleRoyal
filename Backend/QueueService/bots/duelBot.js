"use strict";

const crypto = require("crypto");
const WebSocket = require("ws");

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function httpJson(method, url, body) {
  return new Promise((resolve, reject) => {
    const payload = body ? JSON.stringify(body) : null;
    const parsed = new URL(url);
    const lib = parsed.protocol === "https:" ? require("https") : require("http");
    const req = lib.request(
      {
        hostname: parsed.hostname,
        port: parsed.port,
        path: `${parsed.pathname}${parsed.search}`,
        method,
        headers: payload
          ? {
              "Content-Type": "application/json",
              "Content-Length": Buffer.byteLength(payload),
            }
          : {},
      },
      (res) => {
        let raw = "";
        res.on("data", (chunk) => {
          raw += chunk;
        });
        res.on("end", () => {
          try {
            resolve({ status: res.statusCode, body: raw ? JSON.parse(raw) : {} });
          } catch (error) {
            reject(error);
          }
        });
      }
    );
    req.on("error", reject);
    if (payload) {
      req.write(payload);
    }
    req.end();
  });
}

class DuelBot {
  constructor(options) {
    this.id = options.id || `bot-${crypto.randomUUID().slice(0, 8)}`;
    this.playerId = options.playerId || this.id;
    this.httpBase = options.httpBase;
    this.wsUrl = options.wsUrl;
    this.onLog = options.onLog || (() => {});
    this.running = false;
    this.socket = null;
    this.ticketId = "";
    this.matchId = "";
    this.matchPhase = "lobby";
    this.teamIndex = 0;
    this.poseSeq = 0;
    this.spawnX = -6;
    this.spawnZ = 0;
    this.yaw = 90;
    this.moveAngle = 0;
    this.weaponPicked = false;
    this.loopTimer = null;
  }

  log(message) {
    this.onLog(`[${this.id}] ${message}`);
  }

  async start() {
    if (this.running) {
      return;
    }
    this.running = true;

    while (this.running) {
      try {
        await sleep(250 + Math.floor(Math.random() * 1750));
        await this.runMatchCycle();
      } catch (error) {
        this.log(`error: ${error.message || error}`);
        await sleep(2000);
      }
    }
  }

  stop() {
    this.running = false;
    this.closeSocket();
    if (this.loopTimer) {
      clearInterval(this.loopTimer);
      this.loopTimer = null;
    }
  }

  closeSocket() {
    if (this.socket) {
      try {
        this.socket.close();
      } catch {
        // ignored
      }
      this.socket = null;
    }
  }

  async runMatchCycle() {
    this.ticketId = "";
    this.matchId = "";
    this.matchPhase = "lobby";
    this.weaponPicked = false;
    this.poseSeq = 0;

    const enqueue = await httpJson("POST", `${this.httpBase}/enqueue`, {
      playerId: this.playerId,
      matchMode: "duel",
    });
    if (enqueue.status !== 200 || !enqueue.body.ticketId) {
      throw new Error(`enqueue failed status=${enqueue.status}`);
    }

    this.ticketId = enqueue.body.ticketId;
    this.log(`queued ticket=${this.ticketId.slice(0, 8)}`);

    const deadline = Date.now() + 60000;
    while (Date.now() < deadline) {
      const status = await httpJson("GET", `${this.httpBase}/ticket/${encodeURIComponent(this.ticketId)}`);
      if (status.status !== 200) {
        throw new Error("ticket poll failed");
      }
      if (status.body.status === "Matched" && status.body.matchId) {
        this.matchId = status.body.matchId;
        break;
      }
      if (status.body.status === "Cancelled" || status.body.status === "Finished") {
        await sleep(1000);
        return;
      }
      await sleep(2000);
    }

    if (!this.matchId) {
      throw new Error("matchmaking timeout");
    }

    this.log(`matched match=${this.matchId.slice(0, 8)}`);
    await this.connectAndPlay();
  }

  async connectAndPlay() {
    await new Promise((resolve, reject) => {
      const socket = new WebSocket(this.wsUrl);
      this.socket = socket;
      let joined = false;

      socket.on("open", () => {
        socket.send(JSON.stringify({ type: "join", ticketId: this.ticketId }));
      });

      socket.on("message", (raw) => {
        let message;
        try {
          message = JSON.parse(String(raw));
        } catch {
          return;
        }

        if (!joined && message.type === "joined") {
          joined = true;
          resolve();
          return;
        }

        this.handleServerMessage(message);
      });

      socket.on("error", reject);
      socket.on("close", () => {
        this.socket = null;
        if (!joined) {
          reject(new Error("socket closed before join"));
        }
      });

      setTimeout(() => {
        if (!joined) {
          reject(new Error("join timeout"));
        }
      }, 10000);
    });

    this.loopTimer = setInterval(() => {
      this.tickPose();
    }, 100);

    const endDeadline = Date.now() + 15 * 60 * 1000;
    while (this.running && Date.now() < endDeadline) {
      if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
        break;
      }
      if (this.matchPhase === "ending") {
        break;
      }
      await sleep(500);
    }

    if (this.loopTimer) {
      clearInterval(this.loopTimer);
      this.loopTimer = null;
    }
    this.closeSocket();
    await sleep(1000);
  }

  handleServerMessage(message) {
    if (!message || !message.type) {
      return;
    }

    if (message.type === "match_state") {
      this.matchPhase = message.phase || this.matchPhase;
      if (Number.isFinite(message.duelTeamIndex)) {
        this.teamIndex = message.duelTeamIndex;
        this.spawnX = this.teamIndex === 0 ? -6 : 6;
        this.yaw = this.teamIndex === 0 ? 90 : -90;
      }

      if (message.phase === "round_pick" && !this.weaponPicked) {
        this.weaponPicked = true;
        const kind = Math.floor(Math.random() * 4);
        this.sendJson({
          type: "duel_weapon_pick",
          ticketId: this.ticketId,
          weaponKind: kind,
        });
      }
      return;
    }

    if (message.type === "match_stats" && message.profile) {
      this.log(`rating delta=${message.ratingDelta} duel=${message.profile.duelRating}`);
    }
  }

  sendJson(payload) {
    if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
      return;
    }
    this.socket.send(JSON.stringify(payload));
  }

  tickPose() {
    if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
      return;
    }

    const canMove = this.matchPhase === "prep" || this.matchPhase === "round";
    if (!canMove) {
      return;
    }

    this.moveAngle += 0.04;
    const radius = 4.5;
    const x = this.spawnX + Math.cos(this.moveAngle) * radius;
    const z = Math.sin(this.moveAngle) * radius;
    const moveX = -Math.sin(this.moveAngle);
    const moveZ = Math.cos(this.moveAngle);
    this.yaw = (Math.atan2(moveX, moveZ) * 180) / Math.PI;
    this.poseSeq += 1;

    this.sendJson({
      type: "pose",
      ticketId: this.ticketId,
      seq: this.poseSeq,
      position: { x, y: 0, z },
      yaw: this.yaw,
      lookPitch: 0,
      velX: moveX * 4,
      velY: 0,
      velZ: moveZ * 4,
      isGrounded: true,
      animSpeed: 3.2,
      moveInputX: moveX,
      moveInputZ: moveZ,
      isSprinting: true,
      jumpState: 0,
      animPhase: (this.poseSeq % 120) / 120,
    });
  }
}

module.exports = { DuelBot, httpJson };
