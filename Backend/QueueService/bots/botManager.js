"use strict";

const crypto = require("crypto");
const config = require("../config");
const { DuelBot } = require("./duelBot");

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

class BotManager {
  constructor() {
    this.bots = new Map();
    this.httpBase = `http://${config.botHttpHost}:${config.botHttpPort}`;
    this.wsUrl = `ws://${config.botWsHost}:${config.botWsPort}`;
    this.autoFillTimer = null;
    this.spawnInProgress = false;
  }

  stats() {
    const items = [];
    for (const [id, bot] of this.bots.entries()) {
      items.push({
        id,
        playerId: bot.playerId,
        running: bot.running,
        ticketId: bot.ticketId || "",
        matchId: bot.matchId || "",
        phase: bot.matchPhase || "",
      });
    }
    return {
      activeBots: this.bots.size,
      httpBase: this.httpBase,
      wsUrl: this.wsUrl,
      autoFillTarget: config.botAutoFillTarget,
      spawnInProgress: this.spawnInProgress,
      bots: items,
    };
  }

  startBot(job) {
    const { id, playerId } = job;
    const bot = new DuelBot({
      id,
      playerId,
      httpBase: this.httpBase,
      wsUrl: this.wsUrl,
      onLog: (line) => {
        if (config.debugRealtime) {
          console.log(`[bot] ${line}`);
        }
      },
    });

    this.bots.set(id, bot);
    void bot.start().finally(() => {
      this.bots.delete(id);
    });
    return id;
  }

  async spawn(count) {
    const total = Math.max(0, Math.min(config.botSpawnMax || 500, Math.floor(Number(count) || 0)));
    if (total <= 0) {
      return { spawned: 0, botIds: [] };
    }

    if (this.spawnInProgress) {
      return { spawned: 0, botIds: [], skipped: "spawn_in_progress" };
    }

    this.spawnInProgress = true;
    const created = [];
    const chunkSize = Math.max(5, config.botSpawnChunkSize || 25);

    try {
      for (let offset = 0; offset < total; offset += chunkSize) {
        const batchCount = Math.min(chunkSize, total - offset);
        for (let i = 0; i < batchCount; i++) {
          const id = `bot-${crypto.randomUUID().slice(0, 8)}`;
          const playerId = `bot-player-${id}`;
          created.push(this.startBot({ id, playerId }));
        }

        if (offset + batchCount < total) {
          await sleep(400);
        }
      }
    } finally {
      this.spawnInProgress = false;
    }

    return { spawned: created.length, botIds: created };
  }

  stopAll() {
    for (const bot of this.bots.values()) {
      bot.stop();
    }
    this.bots.clear();
    this.spawnInProgress = false;
    if (this.autoFillTimer) {
      clearInterval(this.autoFillTimer);
      this.autoFillTimer = null;
    }
    return { stopped: true };
  }

  startAutoFill() {
    if (this.autoFillTimer || config.botAutoFillTarget <= 0) {
      return;
    }

    const tick = () => {
      const deficit = config.botAutoFillTarget - this.bots.size;
      if (deficit > 0 && !this.spawnInProgress) {
        const chunk = Math.min(deficit, Math.max(5, config.botSpawnChunkSize || 25));
        void this.spawn(chunk);
      }
    };

    tick();
    this.autoFillTimer = setInterval(tick, 5000);
  }
}

module.exports = BotManager;
