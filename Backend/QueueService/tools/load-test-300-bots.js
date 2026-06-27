#!/usr/bin/env node
"use strict";

const http = require("http");

const baseUrl = process.env.LOAD_TEST_HTTP || "http://127.0.0.1:5050";
const targetBots = Math.max(1, Number(process.env.LOAD_TEST_BOTS || 300));
const pollMs = Math.max(1000, Number(process.env.LOAD_TEST_POLL_MS || 5000));

function request(method, path, body) {
  return new Promise((resolve, reject) => {
    const url = new URL(path, baseUrl);
    const payload = body ? JSON.stringify(body) : null;
    const req = http.request(
      {
        hostname: url.hostname,
        port: url.port,
        path: `${url.pathname}${url.search}`,
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

async function main() {
  console.log(`[load-test] target=${targetBots} base=${baseUrl}`);

  const health0 = await request("GET", "/health");
  console.log("[load-test] health before:", JSON.stringify(health0.body));

  const spawn = await request("POST", "/admin/bots/spawn", { count: targetBots });
  if (spawn.status !== 200) {
    console.error("[load-test] spawn failed", spawn);
    process.exit(1);
  }

  console.log(`[load-test] spawned=${spawn.body.spawned}`);

  const deadline = Date.now() + 10 * 60 * 1000;
  while (Date.now() < deadline) {
    const health = await request("GET", "/health");
    const body = health.body || {};
    console.log(
      `[load-test] players=${body.playersOnline} matches=${body.activeMatches} ` +
      `queue=${body.queueSize} tickDelay=${body.avgTickDelayMs} ` +
      `wsDroppedSnap=${body.ws && body.ws.droppedSnapshots} ` +
      `wsBufferedSoft=${body.ws && body.ws.softLimitBytes}`
    );

    const bots = await request("GET", "/admin/bots/status");
    const activeBots = bots.body && bots.body.activeBots;
    if (Number(activeBots) >= targetBots && Number(body.activeMatches) >= Math.floor(targetBots / 2)) {
      console.log("[load-test] target load reached");
      break;
    }

    await new Promise((resolve) => setTimeout(resolve, pollMs));
  }

  const stop = await request("POST", "/admin/bots/stop");
  console.log("[load-test] stop:", stop.body);
}

main().catch((error) => {
  console.error("[load-test] failed", error);
  process.exit(1);
});
