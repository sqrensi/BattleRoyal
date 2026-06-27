"use strict";

class PerTicketRateLimiter {
  constructor(maxPerSecond) {
    this.maxPerSecond = Math.max(1, maxPerSecond);
    this.buckets = new Map();
  }

  allow(ticketId) {
    const key = String(ticketId || "");
    if (!key) {
      return false;
    }

    const now = Date.now();
    let bucket = this.buckets.get(key);
    if (!bucket || now - bucket.windowStart >= 1000) {
      bucket = { windowStart: now, count: 0 };
      this.buckets.set(key, bucket);
    }

    if (bucket.count >= this.maxPerSecond) {
      return false;
    }

    bucket.count += 1;
    return true;
  }

  prune(maxAgeMs = 120000) {
    const now = Date.now();
    for (const [key, bucket] of this.buckets.entries()) {
      if (now - bucket.windowStart > maxAgeMs) {
        this.buckets.delete(key);
      }
    }
  }
}

module.exports = {
  PerTicketRateLimiter,
};
