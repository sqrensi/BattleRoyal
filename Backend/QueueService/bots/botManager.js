"use strict";

const crypto = require("crypto");
const config = require("../config");
const playerRepository = require("../db/playerRepository");
const { DuelBot } = require("./duelBot");

class BotManager {
  constructor() {
    this.bots = new Map();
    this.httpBase = `http://${config.botHttpHost}:${config.botHttpPort}`;
    this.wsUrl = `ws://${config.botWsHost}:${config.botWsPort}`;
    this.autoFillTimer = null;
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
      bots: items,
    };
  }

  async spawn(count) {
    const total = Math.max(0, Math.min(500, Math.floor(Number(count) || 0)));
    const created = [];

    for (let i = 0; i < total; i++) {
      const id = `bot-${crypto.randomUUID().slice(0, 8)}`;
      const playerId = `bot-player-${id}`;
      await playerRepository.ensurePlayer(playerId);

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
      created.push(id);
      void bot.start().finally(() => {
        this.bots.delete(id);
      });
    }

    return { spawned: created.length, botIds: created };
  }

  stopAll() {
    for (const bot of this.bots.values()) {
      bot.stop();
    }
    this.bots.clear();
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

    this.autoFillTimer = setInterval(() => {
      const deficit = config.botAutoFillTarget - this.bots.size;
      if (deficit > 0) {
        void this.spawn(deficit);
      }
    }, 5000);
  }
}

module.exports = BotManager;
