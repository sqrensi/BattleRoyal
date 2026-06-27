"use strict";

class Metrics {
  constructor() {
    this.startedAtMs = Date.now();
    this.playersOnline = 0;
    this.activeMatches = 0;
    this.queueSize = 0;
    this.workerStats = new Map();
    this.tickDelays = [];
  }

  setQueueSize(size) {
    this.queueSize = size;
  }

  setPlayersOnline(count) {
    this.playersOnline = Math.max(0, count);
  }

  adjustPlayersOnline(delta) {
    this.playersOnline = Math.max(0, this.playersOnline + delta);
  }

  setActiveMatches(count) {
    this.activeMatches = count;
  }

  updateWorker(workerId, patch) {
    const prev = this.workerStats.get(workerId) || {};
    this.workerStats.set(workerId, { ...prev, ...patch, updatedAtMs: Date.now() });
  }

  recordTickDelay(ms) {
    this.tickDelays.push(ms);
    if (this.tickDelays.length > 120) {
      this.tickDelays.shift();
    }
  }

  snapshot() {
    const delays = this.tickDelays.length ? this.tickDelays : [0];
    const avgTickDelay = delays.reduce((a, b) => a + b, 0) / delays.length;
    const mem = process.memoryUsage();
    return {
      uptimeSec: Math.floor((Date.now() - this.startedAtMs) / 1000),
      playersOnline: this.playersOnline,
      activeMatches: this.activeMatches,
      queueSize: this.queueSize,
      avgTickDelayMs: Number(avgTickDelay.toFixed(2)),
      rssMb: Number((mem.rss / 1024 / 1024).toFixed(1)),
      workers: Array.from(this.workerStats.entries()).map(([id, stats]) => ({ id, ...stats })),
    };
  }
}

module.exports = Metrics;
