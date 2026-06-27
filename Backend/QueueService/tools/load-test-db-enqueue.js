#!/usr/bin/env node
"use strict";

const crypto = require("crypto");
const { initDatabase } = require("../db/database");
const playerRepository = require("../db/playerRepository");

const total = Math.max(1, Number(process.env.LOAD_TEST_PLAYERS || 300));
const concurrency = Math.max(1, Number(process.env.LOAD_TEST_CONCURRENCY || 300));
const scenario = String(process.env.LOAD_TEST_SCENARIO || "existing").toLowerCase();

function percentile(sorted, p) {
  if (sorted.length === 0) {
    return 0;
  }
  const idx = Math.min(sorted.length - 1, Math.ceil((p / 100) * sorted.length) - 1);
  return sorted[idx];
}

async function runBatch(jobs) {
  const latencies = [];
  let ok = 0;
  let failed = 0;
  const errors = new Map();

  const startedAt = Date.now();

  await Promise.all(
    jobs.map(async (playerId) => {
      const t0 = Date.now();
      try {
        const profile = await playerRepository.ensurePlayerForQueue(playerId);
        if (!profile || !profile.nickname) {
          throw new Error("empty profile");
        }
        ok += 1;
        latencies.push(Date.now() - t0);
      } catch (error) {
        failed += 1;
        const key = error && error.message ? error.message : String(error);
        errors.set(key, (errors.get(key) || 0) + 1);
      }
    })
  );

  latencies.sort((a, b) => a - b);
  const elapsedMs = Date.now() - startedAt;

  return {
    total: jobs.length,
    ok,
    failed,
    elapsedMs,
    rps: elapsedMs > 0 ? Number(((jobs.length / elapsedMs) * 1000).toFixed(1)) : 0,
    p50: percentile(latencies, 50),
    p95: percentile(latencies, 95),
    p99: percentile(latencies, 99),
    max: latencies.length > 0 ? latencies[latencies.length - 1] : 0,
    errors: Object.fromEntries(errors),
  };
}

async function warmupExisting(count) {
  const ids = [];
  for (let i = 0; i < count; i++) {
    const id = `loadtest-warm-${i}-${crypto.randomUUID().slice(0, 8)}`;
    await playerRepository.ensurePlayerForQueue(id);
    ids.push(id);
  }
  return ids;
}

async function main() {
  const dbInfo = await initDatabase();
  console.log(`[db-load] driver=${dbInfo.driver} scenario=${scenario} total=${total} concurrency=${concurrency}`);
  console.log(
    `[db-load] pool max=${process.env.DB_POOL_MAX || 20} ` +
    `queueDb=${process.env.QUEUE_DB_CONCURRENCY || 15} ` +
    `maintenance=${process.env.MAINTENANCE_DB_CONCURRENCY || 5}`
  );

  if (scenario === "bots") {
    const jobs = Array.from({ length: total }, (_, i) => `bot-player-bot-${String(i).padStart(4, "0")}`);
    const result = await runBatch(jobs);
    console.log("[db-load] bots (should be ~0ms, no DB):", JSON.stringify(result, null, 2));
    process.exit(result.failed > 0 ? 1 : 0);
  }

  if (scenario === "existing") {
    const warmCount = Math.min(total, 300);
    console.log(`[db-load] warming ${warmCount} existing players...`);
    const existingIds = await warmupExisting(warmCount);
    const jobs = Array.from({ length: total }, (_, i) => existingIds[i % existingIds.length]);
    const result = await runBatch(jobs);
    console.log("[db-load] existing players:", JSON.stringify(result, null, 2));
    process.exit(result.failed > 0 ? 1 : 0);
  }

  if (scenario === "new") {
    const jobs = Array.from(
      { length: total },
      () => `loadtest-new-${crypto.randomUUID()}`
    );
    const result = await runBatch(jobs);
    console.log("[db-load] brand-new players:", JSON.stringify(result, null, 2));
    process.exit(result.failed > 0 ? 1 : 0);
  }

  if (scenario === "mixed") {
    console.log("[db-load] mixed: 1/3 bots, 1/3 existing, 1/3 new");
    const existingIds = await warmupExisting(Math.ceil(total / 3));
    const jobs = [];
    for (let i = 0; i < total; i++) {
      const bucket = i % 3;
      if (bucket === 0) {
        jobs.push(`bot-player-mix-${i}`);
      } else if (bucket === 1) {
        jobs.push(existingIds[i % existingIds.length]);
      } else {
        jobs.push(`loadtest-mixed-new-${i}-${crypto.randomUUID().slice(0, 8)}`);
      }
    }
    const result = await runBatch(jobs);
    console.log("[db-load] mixed:", JSON.stringify(result, null, 2));
    process.exit(result.failed > 0 ? 1 : 0);
  }

  console.error(`[db-load] unknown scenario: ${scenario}`);
  process.exit(1);
}

main().catch((error) => {
  console.error("[db-load] fatal", error);
  process.exit(1);
});
