#!/usr/bin/env node
"use strict";

const crypto = require("crypto");
const http = require("http");
const https = require("https");

const baseUrl = process.env.LOAD_TEST_HTTP || "http://api.game35.ru:5050";
const total = Math.max(1, Number(process.env.LOAD_TEST_PLAYERS || 300));
const scenario = String(process.env.LOAD_TEST_SCENARIO || "existing").toLowerCase();
const prefix = process.env.LOAD_TEST_PREFIX || `loadtest-${Date.now()}`;

function percentile(sorted, p) {
  if (sorted.length === 0) {
    return 0;
  }
  const idx = Math.min(sorted.length - 1, Math.ceil((p / 100) * sorted.length) - 1);
  return sorted[idx];
}

function request(method, path, body) {
  return new Promise((resolve, reject) => {
    const url = new URL(path, baseUrl);
    const lib = url.protocol === "https:" ? https : http;
    const payload = body ? JSON.stringify(body) : null;
    const t0 = Date.now();
    const req = lib.request(
      {
        hostname: url.hostname,
        port: url.port || (url.protocol === "https:" ? 443 : 80),
        path: `${url.pathname}${url.search}`,
        method,
        headers: payload
          ? {
              "Content-Type": "application/json",
              "Content-Length": Buffer.byteLength(payload),
            }
          : {},
        timeout: 30000,
      },
      (res) => {
        let raw = "";
        res.on("data", (chunk) => {
          raw += chunk;
        });
        res.on("end", () => {
          let parsed = {};
          try {
            parsed = raw ? JSON.parse(raw) : {};
          } catch (error) {
            parsed = { raw };
          }
          resolve({
            status: res.statusCode,
            body: parsed,
            latencyMs: Date.now() - t0,
          });
        });
      }
    );
    req.on("timeout", () => {
      req.destroy(new Error("timeout"));
    });
    req.on("error", reject);
    if (payload) {
      req.write(payload);
    }
    req.end();
  });
}

async function enqueue(playerId) {
  return request("POST", "/enqueue", { playerId, matchMode: "duel" });
}

async function health() {
  const res = await request("GET", "/health");
  return res.body || {};
}

async function runBatch(playerIds) {
  const latencies = [];
  let ok = 0;
  let failed = 0;
  const errors = new Map();
  const tickets = [];

  const startedAt = Date.now();

  const results = await Promise.allSettled(playerIds.map((playerId) => enqueue(playerId)));

  for (const result of results) {
    if (result.status === "rejected") {
      failed += 1;
      const key = result.reason && result.reason.message ? result.reason.message : String(result.reason);
      errors.set(key, (errors.get(key) || 0) + 1);
      continue;
    }

    const res = result.value;
    latencies.push(res.latencyMs);
    if (res.status === 200 && res.body && res.body.ticketId) {
      ok += 1;
      tickets.push(res.body.ticketId);
    } else {
      failed += 1;
      const key = `${res.status}:${res.body && res.body.error ? res.body.error : "bad_response"}`;
      errors.set(key, (errors.get(key) || 0) + 1);
    }
  }

  latencies.sort((a, b) => a - b);

  return {
    total: playerIds.length,
    ok,
    failed,
    elapsedMs: Date.now() - startedAt,
    rps: Number(((playerIds.length / Math.max(1, Date.now() - startedAt)) * 1000).toFixed(1)),
    p50: percentile(latencies, 50),
    p95: percentile(latencies, 95),
    p99: percentile(latencies, 99),
    max: latencies.length > 0 ? latencies[latencies.length - 1] : 0,
    errors: Object.fromEntries(errors),
    tickets,
  };
}

async function warmupExisting(count) {
  const ids = [];
  for (let i = 0; i < count; i++) {
    const id = `${prefix}-warm-${i}`;
    const res = await enqueue(id);
    if (res.status !== 200) {
      throw new Error(`warmup failed ${id}: ${res.status}`);
    }
    ids.push(id);
  }
  return ids;
}

async function cleanupTickets(ticketIds) {
  let cancelled = 0;
  await Promise.all(
    ticketIds.map(async (ticketId) => {
      try {
        const res = await request("POST", "/dequeue", { ticketId });
        if (res.status === 200) {
          cancelled += 1;
        }
      } catch {
        // ignore
      }
    })
  );
  return cancelled;
}

async function main() {
  console.log(`[http-db-load] base=${baseUrl} scenario=${scenario} total=${total} prefix=${prefix}`);

  const before = await health();
  console.log("[http-db-load] health before:", JSON.stringify({
    ok: before.ok,
    database: before.database,
    queueSize: before.queueSize,
    activeMatches: before.activeMatches,
    uptimeSec: before.uptimeSec,
  }));

  if (!before.ok) {
    console.error("[http-db-load] server not healthy, abort");
    process.exit(1);
  }

  let result;
  if (scenario === "bots") {
    const ids = Array.from({ length: total }, (_, i) => `bot-player-http-${prefix}-${i}`);
    result = await runBatch(ids);
    console.log("[http-db-load] bots:", JSON.stringify(result, null, 2));
  } else if (scenario === "existing") {
    const warmCount = Math.min(total, 100);
    console.log(`[http-db-load] warming ${warmCount} players sequentially...`);
    const existingIds = await warmupExisting(warmCount);
    const jobs = Array.from({ length: total }, (_, i) => existingIds[i % existingIds.length]);
    result = await runBatch(jobs);
    console.log("[http-db-load] existing players (parallel):", JSON.stringify({
      total: result.total,
      ok: result.ok,
      failed: result.failed,
      elapsedMs: result.elapsedMs,
      rps: result.rps,
      p50: result.p50,
      p95: result.p95,
      p99: result.p99,
      max: result.max,
      errors: result.errors,
    }, null, 2));
  } else if (scenario === "new") {
    const ids = Array.from({ length: total }, (_, i) => `${prefix}-new-${i}-${crypto.randomUUID().slice(0, 8)}`);
    result = await runBatch(ids);
    console.log("[http-db-load] brand-new players:", JSON.stringify({
      total: result.total,
      ok: result.ok,
      failed: result.failed,
      elapsedMs: result.elapsedMs,
      rps: result.rps,
      p50: result.p50,
      p95: result.p95,
      p99: result.p99,
      max: result.max,
      errors: result.errors,
    }, null, 2));
  } else if (scenario === "mixed") {
    const warmCount = Math.ceil(total / 3);
    console.log(`[http-db-load] warming ${warmCount} for mixed test...`);
    const existingIds = await warmupExisting(warmCount);
    const jobs = [];
    for (let i = 0; i < total; i++) {
      const bucket = i % 3;
      if (bucket === 0) {
        jobs.push(`bot-player-mix-${prefix}-${i}`);
      } else if (bucket === 1) {
        jobs.push(existingIds[i % existingIds.length]);
      } else {
        jobs.push(`${prefix}-mixed-new-${i}-${crypto.randomUUID().slice(0, 8)}`);
      }
    }
    result = await runBatch(jobs);
    console.log("[http-db-load] mixed:", JSON.stringify({
      total: result.total,
      ok: result.ok,
      failed: result.failed,
      elapsedMs: result.elapsedMs,
      rps: result.rps,
      p50: result.p50,
      p95: result.p95,
      p99: result.p99,
      max: result.max,
      errors: result.errors,
    }, null, 2));
  } else {
    console.error(`[http-db-load] unknown scenario: ${scenario}`);
    process.exit(1);
  }

  const after = await health();
  console.log("[http-db-load] health after:", JSON.stringify({
    ok: after.ok,
    database: after.database,
    queueSize: after.queueSize,
    activeMatches: after.activeMatches,
    uptimeSec: after.uptimeSec,
    rssMb: after.rssMb,
  }));

  if (result.tickets && result.tickets.length > 0) {
    const cancelled = await cleanupTickets(result.tickets.slice(0, 50));
    console.log(`[http-db-load] dequeued sample tickets: ${cancelled}/50`);
  }

  const crashed = !after.ok;
  const tooManyFailures = result.failed > Math.max(5, Math.floor(result.total * 0.05));
  process.exit(crashed || tooManyFailures ? 1 : 0);
}

main().catch((error) => {
  console.error("[http-db-load] fatal", error);
  process.exit(1);
});
