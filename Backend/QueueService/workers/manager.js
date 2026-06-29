"use strict";

const path = require("path");
const { Worker } = require("worker_threads");
const config = require("../config");
const log = require("../lib/logger");
const { MasterToWorker, WorkerToMaster } = require("./protocol");

class WorkerSlot {
  constructor(workerId, worker, onMessage) {
    this.workerId = workerId;
    this.worker = worker;
    this.matchCount = 0;
    this.matchIds = new Set();
    this.ready = false;
    this.restarting = false;

    worker.on("message", (msg) => onMessage(workerId, msg));
    worker.on("error", (err) => {
      const detail = err && (err.stack || err.message) ? (err.stack || err.message) : String(err);
      log.error("worker", `worker ${workerId} error ${detail}`);
    });
    worker.on("exit", (code) => {
      if (code !== 0) {
        log.warn("worker", `worker ${workerId} exited code=${code}`);
      }
    });
  }
}

class WorkerManager {
  constructor(workerCount, options) {
    this.workerCount = workerCount;
    this.options = options || {};
    this.slots = [];
    this.matchToWorker = new Map();
    this.handlers = {
      onWorkerMessage: () => {},
      onWorkerReady: () => {},
    };
    this.shuttingDown = false;

    for (let i = 0; i < workerCount; i++) {
      this.spawnWorker(i);
    }
  }

  onMessage(handler) {
    this.handlers.onWorkerMessage = handler;
  }

  onWorkerReady(handler) {
    this.handlers.onWorkerReady = handler;
  }

  workerData(workerId) {
    return {
      workerId,
      tickRateHz: config.tickRateHz,
      snapshotRateHz: config.snapshotRateHz,
      poseSampleRateHz: config.maxPoseMessagesPerSecond,
      snapshotHistorySamples: config.snapshotHistorySamples,
      useBinarySnapshots: config.useBinarySnapshots,
      roundsToWin: config.roundsToWin,
      weaponPickTimeoutMs: config.weaponPickTimeoutMs,
      roundTimeoutMs: config.roundTimeoutMs,
      roundEndTimeoutMs: config.roundEndTimeoutMs,
      prepTimeoutMs: config.prepTimeoutMs,
      matchJoinTimeoutMs: config.matchTimeoutSeconds * 1000,
      dmMinPlayers: config.dmMinPlayers,
      dmMaxPlayers: config.dmMaxPlayers,
      dmMatchDurationMs: config.dmMatchDurationMs,
      dmRespawnDelayMs: config.dmRespawnDelayMs,
      aiThinkHz: config.aiThinkHz,
      maxPlayerSpeed: config.maxPlayerSpeed,
      maxTeleportDistance: config.maxTeleportDistance,
      playerHitRadius: config.playerHitRadius,
    };
  }

  spawnWorker(workerId) {
    const worker = new Worker(path.join(__dirname, "gameWorker.js"), {
      workerData: this.workerData(workerId),
    });
    const slot = new WorkerSlot(workerId, worker, (id, msg) => this.handleWorkerMessage(id, msg));
    const existingIndex = this.slots.findIndex((s) => s.workerId === workerId);
    if (existingIndex >= 0) {
      this.slots[existingIndex] = slot;
    } else {
      this.slots.push(slot);
    }
    return slot;
  }

  handleWorkerMessage(workerId, msg) {
    if (!msg || !msg.type) {
      return;
    }

    if (msg.type === WorkerToMaster.WORKER_READY) {
      const slot = this.slots.find((s) => s.workerId === workerId);
      if (slot) {
        slot.ready = !msg.shuttingDown;
      }
      if (!msg.shuttingDown) {
        this.handlers.onWorkerReady(workerId);
      }
      return;
    }

    if (msg.type === WorkerToMaster.MATCH_FINISHED) {
      const slot = this.slots.find((s) => s.workerId === workerId);
      if (slot && msg.matchId) {
        slot.matchIds.delete(msg.matchId);
        slot.matchCount = slot.matchIds.size;
        this.matchToWorker.delete(msg.matchId);
      }
    }

    this.handlers.onWorkerMessage(workerId, msg);
  }

  pickSlot() {
    const live = this.slots.filter((s) => s.ready && !s.restarting);
    if (!live.length) {
      return null;
    }
    live.sort((a, b) => a.matchCount - b.matchCount);
    const slot = live[0];
    if (slot.matchCount >= config.maxMatchesPerWorker) {
      return null;
    }
    return slot;
  }

  assign(match) {
    const slot = this.pickSlot();
    if (!slot) {
      return false;
    }

    slot.matchCount += 1;
    slot.matchIds.add(match.id);
    this.matchToWorker.set(match.id, slot.workerId);

    slot.worker.postMessage({
      type: MasterToWorker.CREATE_MATCH,
      match,
    });

    return true;
  }

  getWorkerIdForMatch(matchId) {
    return this.matchToWorker.get(matchId);
  }

  sendPlayerEvent(matchId, ticketId, eventType, payload) {
    const workerId = this.matchToWorker.get(matchId);
    if (workerId === undefined) {
      return false;
    }
    const slot = this.slots.find((s) => s.workerId === workerId);
    if (!slot) {
      return false;
    }
    slot.worker.postMessage({
      type: MasterToWorker.PLAYER_EVENT,
      matchId,
      ticketId,
      eventType,
      payload,
    });
    return true;
  }

  sendDisconnect(matchId, ticketId) {
    const workerId = this.matchToWorker.get(matchId);
    if (workerId === undefined) {
      return false;
    }
    const slot = this.slots.find((s) => s.workerId === workerId);
    if (!slot) {
      return false;
    }
    slot.worker.postMessage({
      type: MasterToWorker.PLAYER_DISCONNECT,
      matchId,
      ticketId,
    });
    return true;
  }

  restartWorker(workerId) {
    const slot = this.slots.find((s) => s.workerId === workerId);
    if (!slot || slot.restarting) {
      return;
    }
    slot.restarting = true;
    log.warn("worker", `restarting worker ${workerId}`);

    for (const matchId of Array.from(slot.matchIds)) {
      this.matchToWorker.delete(matchId);
      this.handlers.onWorkerMessage(workerId, {
        type: WorkerToMaster.MATCH_FINISHED,
        matchId,
        winnerTicketId: "",
        abandoned: true,
      });
    }
    slot.matchIds.clear();
    slot.matchCount = 0;

    try {
      slot.worker.terminate();
    } catch {
      // ignored
    }

    setTimeout(() => {
      this.spawnWorker(workerId);
      const fresh = this.slots.find((s) => s.workerId === workerId);
      if (fresh) {
        fresh.restarting = false;
      }
    }, 250);
  }

  shutdown() {
    this.shuttingDown = true;
    for (const slot of this.slots) {
      try {
        slot.worker.postMessage({ type: MasterToWorker.SHUTDOWN });
        slot.worker.terminate();
      } catch {
        // ignored
      }
    }
  }

  stats() {
    return this.slots.map((s) => ({
      workerId: s.workerId,
      ready: s.ready,
      matchCount: s.matchCount,
      restarting: s.restarting,
    }));
  }
}

module.exports = WorkerManager;
