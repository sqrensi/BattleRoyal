"use strict";

const crypto = require("crypto");
const { generateAiBotProfile } = require("../workers/aiBotProfile");

class AiBotManager {
  constructor(options) {
    this.createTicket = options.createTicket;
    this.enqueueTicket = options.enqueueTicket;
    this.getQueue = options.getQueue;
    this.isHumanTicket = options.isHumanTicket;
    this.isAiBotTicket = options.isAiBotTicket;
    this.tryMatchQueue = options.tryMatchQueue;
    this.ensurePlayer = options.ensurePlayer;
    this.soloWaitMs = options.soloWaitMs || 2500;
    this.autoFillSolo = options.autoFillSolo !== false;
    this.fillTimer = null;
    this.spawned = 0;
  }

  stats() {
    const queue = this.getQueue();
    const aiInQueue = queue.filter((ticket) => this.isAiBotTicket(ticket)).length;
    const humansWaiting = queue.filter((ticket) => this.isHumanTicket(ticket)).length;
    return {
      spawnedTotal: this.spawned,
      aiInQueue,
      humansWaiting,
      autoFillSolo: this.autoFillSolo,
      soloWaitMs: this.soloWaitMs,
    };
  }

  async createAiTicket() {
    const id = crypto.randomUUID().slice(0, 8);
    const playerId = `ai-bot-player-${id}`;
    await this.ensurePlayer(playerId);
    const profile = generateAiBotProfile();
    const ticket = this.createTicket(playerId, "duel", profile.nickname);
    ticket.isAiBot = true;
    ticket.aiProfile = profile;
    this.spawned += 1;
    return ticket;
  }

  async spawn(count) {
    const total = Math.max(0, Math.min(500, Math.floor(Number(count) || 0)));
    const created = [];
    const batchSize = 25;

    for (let offset = 0; offset < total; offset += batchSize) {
      const batchCount = Math.min(batchSize, total - offset);
      const tickets = await Promise.all(
        Array.from({ length: batchCount }, () => this.createAiTicket())
      );

      for (const ticket of tickets) {
        this.enqueueTicket(ticket);
        created.push({
          ticketId: ticket.ticketId,
          playerId: ticket.playerId,
          nickname: ticket.nickname,
        });
      }
    }

    await this.tryMatchQueue();
    return { spawned: created.length, tickets: created };
  }

  async fillSoloHumans() {
    const queue = this.getQueue();
    const nowMs = Date.now();
    let injected = 0;

    for (const ticket of queue) {
      if (!this.isHumanTicket(ticket)) {
        continue;
      }

      const waitingMs = nowMs - ticket.createdAtMs;
      if (waitingMs < this.soloWaitMs) {
        continue;
      }

      const hasPartner = queue.some(
        (other) => other !== ticket && this.canPair(ticket, other)
      );
      if (hasPartner) {
        continue;
      }

      const aiTicket = await this.createAiTicket();
      queue.push(aiTicket);
      injected += 1;
    }

    if (injected > 0) {
      await this.tryMatchQueue();
    }

    return { injected };
  }

  canPair(a, b) {
    if (!a || !b) {
      return false;
    }
    const aiA = this.isAiBotTicket(a);
    const aiB = this.isAiBotTicket(b);
    if (aiA && aiB) {
      return true;
    }
    if (aiA !== aiB) {
      return true;
    }
    return this.isHumanTicket(a) && this.isHumanTicket(b);
  }

  startAutoFill() {
    if (this.fillTimer || !this.autoFillSolo) {
      return;
    }

    this.fillTimer = setInterval(() => {
      void this.fillSoloHumans();
    }, 1500);
  }

  stop() {
    if (this.fillTimer) {
      clearInterval(this.fillTimer);
      this.fillTimer = null;
    }

    const queue = this.getQueue();
    let removed = 0;
    for (let i = queue.length - 1; i >= 0; i--) {
      if (this.isAiBotTicket(queue[i]) && queue[i].status === "Queued") {
        queue.splice(i, 1);
        removed += 1;
      }
    }

    return { stopped: true, removedFromQueue: removed };
  }
}

module.exports = AiBotManager;
