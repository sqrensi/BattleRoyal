#!/usr/bin/env node
"use strict";

const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const outFile = path.join(root, "DUEL_SERVER_ARCHITECTURE_AND_CODE.txt");

const EXCLUDE_PATH_PARTS = [
  "node_modules",
  ".git",
  "DUEL_SERVER_ARCHITECTURE_AND_CODE.txt",
  "tools/export-duel-architecture.js",
  "tools/refactor-server-duel-only.js"
];

const EXCLUDE_FILE_NAMES = ["package-lock.json"];

const ROOT_FILES = [
  "server.js",
  "duelMatch.js",
  "package.json",
  "README.md",
  "DEPLOY-VPS.md",
  "env.vps.example",
  "YANDEX-GAMES-HTTPS-CSP.md"
];

function shouldExclude(relPath) {
  const normalized = relPath.replace(/\\/g, "/");
  if (EXCLUDE_FILE_NAMES.includes(path.basename(normalized))) {
    return true;
  }
  return EXCLUDE_PATH_PARTS.some((part) => normalized.includes(part));
}

function walkDir(absDir, relPrefix, out) {
  if (!fs.existsSync(absDir)) {
    return;
  }
  const entries = fs.readdirSync(absDir, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name));
  for (const entry of entries) {
    const rel = relPrefix ? `${relPrefix}/${entry.name}` : entry.name;
    if (shouldExclude(rel)) {
      continue;
    }
    const abs = path.join(absDir, entry.name);
    if (entry.isDirectory()) {
      out.tree.push(`${rel}/`);
      walkDir(abs, rel, out);
    } else if (/\.(js|json|sql|md|example|conf)$/i.test(entry.name)) {
      out.tree.push(rel);
      out.files.push(rel);
    }
  }
}

function readText(rel) {
  const fp = path.join(root, rel);
  return fs.existsSync(fp) ? fs.readFileSync(fp, "utf8") : null;
}

function parseFunctions(source) {
  const rows = [];
  for (const match of source.matchAll(/^function\s+([A-Za-z0-9_]+)/gm)) {
    rows.push({ name: match[1], line: source.slice(0, match.index).split("\n").length });
  }
  return rows;
}

function buildArchitectureOverview(serverJs, duelJs) {
  const lines = [];
  lines.push("ARCHITECTURE — DUEL 1v1 ONLY (Battle Royale removed)");
  lines.push("=".repeat(60));
  lines.push("");
  lines.push("STACK: Node.js, ws, PostgreSQL/SQLite, HTTP :5050 + WS :5051");
  lines.push("");
  lines.push("FILES");
  lines.push("  server.js       — matchmaking, realtime, combat, snapshots");
  lines.push("  duelMatch.js    — duel phases, rounds, weapon pick");
  lines.push("  db/*            — profiles, economy, leaderboard");
  lines.push("  data/*          — catalogs");
  lines.push("");
  lines.push("DUEL FLOW");
  lines.push("  POST /enqueue { matchMode: \"duel\" }");
  lines.push("  GET /ticket/:id → Matched");
  lines.push("  WS join → duel_weapon_pick → pose/shot/hit");
  lines.push("  duelMatch.tickDuelMatches in maintenance sweep");
  lines.push("");
  lines.push("HTTP: /health, /enqueue, /dequeue, /ticket/*, /match/*, /profile/*");
  lines.push("WS: join, pose, shot, hit, pickup, weapon_*, medkit_*, duel_weapon_pick, ping");
  lines.push("");
  lines.push("SERVER.JS FUNCTIONS");
  lines.push("-".repeat(60));
  for (const row of parseFunctions(serverJs)) {
    lines.push(`  L${String(row.line).padStart(4)}  ${row.name}()`);
  }
  lines.push("");
  lines.push("DUELMATCH.JS FUNCTIONS");
  lines.push("-".repeat(60));
  for (const match of duelJs.match(/^function\s+([A-Za-z0-9_]+)/gm) || []) {
    lines.push(`  ${match.replace("function ", "")}()`);
  }
  return lines.join("\n");
}

const bundle = { tree: [], files: [] };
for (const rel of ["db", "data", "deploy", "tools", "tests"]) {
  walkDir(path.join(root, rel), rel, bundle);
}
for (const rel of ROOT_FILES) {
  if (!shouldExclude(rel) && fs.existsSync(path.join(root, rel))) {
    bundle.tree.push(rel);
    bundle.files.push(rel);
  }
}
bundle.tree = [...new Set(bundle.tree)].sort();
bundle.files = [...new Set(bundle.files)].sort();

const serverJs = readText("server.js") || "";
const duelJs = readText("duelMatch.js") || "";

const parts = [];
parts.push("SHOOTERPROTOTYPE — DUEL SERVER ARCHITECTURE & SOURCE CODE");
parts.push(`Generated: ${new Date().toISOString()}`);
parts.push("");
parts.push("SCOPE: duel 1v1 only. Battle Royale code removed from server.");
parts.push("");
parts.push(buildArchitectureOverview(serverJs, duelJs));
parts.push("");
parts.push("DIRECTORY TREE");
parts.push("-".repeat(60));
parts.push(bundle.tree.join("\n"));

for (const rel of bundle.files) {
  parts.push("");
  parts.push("=".repeat(88));
  parts.push(`FILE: ${rel}`);
  parts.push("=".repeat(88));
  parts.push(readText(rel) || "(missing)");
}

fs.writeFileSync(outFile, parts.join("\n"), "utf8");
const mb = (fs.statSync(outFile).size / 1024 / 1024).toFixed(2);
console.log(`Wrote ${outFile} (${mb} MB, ${bundle.files.length} files)`);
