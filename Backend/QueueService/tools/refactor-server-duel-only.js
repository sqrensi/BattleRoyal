#!/usr/bin/env node
"use strict";

const fs = require("fs");
const path = require("path");

const serverPath = path.join(__dirname, "..", "server.js");

const REMOVE_FUNCTIONS = new Set([
  "shouldLockBattleRoyaleDeathState",
  "createBattleRoyaleState",
  "ensureBattleRoyaleState",
  "rollPhase1Center",
  "rollPhase2Center",
  "ensureMatchDamageZone",
  "smoothPhaseProgress",
  "computeZonePhaseProgress",
  "computeTwoPhaseDurations",
  "resolveTotalShrinkDuration",
  "computeTwoPhaseZoneState",
  "computeZoneRadius",
  "computeZoneDamagePerSecond",
  "buildZoneStatePayload",
  "sendZoneStateToSocket",
  "broadcastZoneState",
  "handleWsRegisterDamageZone",
  "sendZoneDamageToSocket",
  "flushZoneDamageOutbound",
  "applyZoneDamageToTicket",
  "tickDamageZones",
  "isBattleRoyaleOnPlane",
  "isBattleRoyaleParachuting",
  "isBattleRoyaleRespawnBlocked",
  "isBattleRoyaleCombatAllowed",
  "getBattleRoyaleKillCountForTicket",
  "recordBattleRoyaleKill",
  "maybeStartBattleRoyaleCountdown",
  "assignBattleRoyalePlaneSpawns",
  "computeBattleRoyalePlaneDurationSeconds",
  "startBattleRoyalePlane",
  "startBattleRoyalePlaying",
  "startBattleRoyaleEnding",
  "onBattleRoyalePlayerDied",
  "handleWsDeathFallLanded",
  "checkBattleRoyaleWinner",
  "computeMapSquareEdgePoint",
  "clampPositionToMapSquare",
  "updateBattleRoyalePlaneRoute",
  "rollAutoDropPositionAtPlaneRouteEnd",
  "ensureAutoDropPosition",
  "hasPlaneReachedRouteEnd",
  "hasPlaneCrossedMapExitEdge",
  "shouldForcePlaneJump",
  "onBattleRoyalePlayerLanded",
  "handleWsPlaneLanded",
  "handleWsPlayerLand",
  "handleWsPlaneJump",
  "buildMatchStatePayload",
  "forceBattleRoyaleDisconnect",
  "syncBattleRoyalePresence",
  "resolveBattleRoyaleCountdownMinPlayers",
  "handleBattleRoyalePlayerLeft",
  "tickBattleRoyaleMatches"
]);

const BR_WS_TYPES = [
  "register_damage_zone",
  "plane_jump",
  "plane_landed",
  "player_land",
  "death_fall_landed"
];

const DUEL_HELPERS = `
function isMatchCombatAllowed(ticket) {
  if (!ticket || !ticket.matchId) {
    return false;
  }

  const session = matchesById.get(ticket.matchId);
  if (session && duelMatch.isDuelSession(session)) {
    return duelMatch.isDuelCombatAllowed(ticket);
  }

  return true;
}

function buildMatchStatePayload(session, ticketId) {
  return duelMatch.buildDuelMatchStatePayload(session, ticketId);
}

function sendMatchStateToSocket(socket, session, ticketId) {
  if (!socket || socket.readyState !== WebSocket.OPEN || !session) {
    return;
  }

  if (!duelMatch.isDuelSession(session)) {
    return;
  }

  try {
    socket.send(JSON.stringify(buildMatchStatePayload(session, ticketId)));
  } catch {
    // ignored
  }
}

function sendMatchStateToTicket(ticketId) {
  const ticket = ticketsById.get(ticketId);
  if (!ticket || !ticket.matchId) {
    return;
  }

  const session = matchesById.get(ticket.matchId);
  const socket = wsClientsByTicketId.get(ticketId);
  sendMatchStateToSocket(socket, session, ticketId);
}

function broadcastMatchState(matchId) {
  const session = matchesById.get(matchId);
  if (!session || !duelMatch.isDuelSession(session)) {
    return;
  }

  const duel = duelMatch.ensureDuelState(session);
  if (duel) {
    duel.lastStateBroadcastMs = Date.now();
  }

  for (const ticketId of session.ticketIds) {
    const socket = wsClientsByTicketId.get(ticketId);
    sendMatchStateToSocket(socket, session, ticketId);
  }
}

function tickActiveMatchPhases(nowMs) {
  duelMatch.tickDuelMatches(nowMs);
}
`;

function findTopLevelFunctions(lines) {
  const functions = [];
  for (let i = 0; i < lines.length; i++) {
    const match = lines[i].match(/^function ([A-Za-z0-9_]+)/);
    if (match) {
      functions.push({ name: match[1], start: i });
    }
  }
  for (let j = 0; j < functions.length; j++) {
    functions[j].end = j + 1 < functions.length ? functions[j + 1].start - 1 : lines.length - 1;
  }
  return functions;
}

function removeBrWsHandlerBlocks(lines) {
  const out = [];
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    if (BR_WS_TYPES.some((type) => line.includes(`message.type === "${type}"`))) {
      let depth = 0;
      do {
        depth += (lines[i].match(/\{/g) || []).length;
        depth -= (lines[i].match(/\}/g) || []).length;
        i++;
      } while (i < lines.length && depth > 0);
      continue;
    }
    out.push(line);
  }
  return out;
}

function refactor(source) {
  let lines = source.replace(/\r\n/g, "\n").split("\n");
  const skip = new Set();

  for (const fn of findTopLevelFunctions(lines)) {
    if (REMOVE_FUNCTIONS.has(fn.name)) {
      for (let i = fn.start; i <= fn.end; i++) {
        skip.add(i);
      }
    }
  }

  const kept = [];
  for (let i = 0; i < lines.length; i++) {
    if (skip.has(i)) {
      continue;
    }
    const line = lines[i];
    if (/^const (BR_|ZONE_)/.test(line)) {
      continue;
    }
    if (/^const (BATTLE_ROYALE_ENABLED|MAX_CONCURRENT_BR_MATCHES|BR_COUNTDOWN|BR_FUTURE|BR_USE_FUTURE)/.test(line)) {
      continue;
    }
    if (line.includes("const matchDamageZonesByMatchId")) {
      continue;
    }
    if (/^\s*tickDamageZones\(\);/.test(line)) {
      continue;
    }
    if (/^\s*matchDamageZonesByMatchId\.delete/.test(line)) {
      continue;
    }
    if (/^\s*sendZoneStateToSocket\(/.test(line)) {
      continue;
    }
    kept.push(line);
  }

  lines = removeBrWsHandlerBlocks(kept);
  let result = lines.join("\n");

  result = result.replace(
    /function normalizeMatchMode\(value\) \{[\s\S]*?\n\}/,
    `function normalizeMatchMode(value) {
  const raw = String(value || "duel").trim().toLowerCase();
  if (raw === "training") {
    return "training";
  }
  if (raw === "challenge") {
    return "challenge";
  }
  if (raw === "duel" || raw === "1v1" || raw === "duel_1v1") {
    return "duel";
  }
  return "duel";
}`
  );

  result = result.replace(
    /if \(normalized === "battle_royale"\) \{\s*return MAX_CONCURRENT_BR_MATCHES;\s*\}/,
    ""
  );

  result = result.replace(
    /function isMatchRespawnBlocked\(ticket\) \{[\s\S]*?\n\}/,
    `function isMatchRespawnBlocked(ticket) {
  if (!ticket || !ticket.matchId) {
    return false;
  }

  return duelMatch.isDuelRespawnBlocked(ticket);
}`
  );

  result = result.replace(
    /matchMode: "battle_royale",\s*\n\s*ticketIds: new Set\(\),\s*\n\s*br: createBattleRoyaleState\(\)/,
    'matchMode: "duel",\n    ticketIds: new Set()'
  );

  result = result.replace(
    "tickBattleRoyaleMatches(Date.now());",
    "tickActiveMatchPhases(Date.now());"
  );

  result = result.replace(/\n\s*if \(BATTLE_ROYALE_ENABLED\) \{\s*\n\s*tryMatchModeBucket\("battle_royale", nowMs\);\s*\n\s*\}/, "");

  result = result.replace(
    /\n\s*if \(!BATTLE_ROYALE_ENABLED && requestedMode === "battle_royale"\) \{[\s\S]*?\n\s*return;\s*\n\s*\}/,
    ""
  );

  result = result.replace(/\n\s*activeBattleRoyaleMatches: countActiveSessionsByMode\("battle_royale"\),/, "");
  result = result.replace(/\n\s*battleRoyaleEnabled: BATTLE_ROYALE_ENABLED,/, "");
  result = result.replace(
    /\n\s*maxConcurrentBattleRoyaleMatches: BATTLE_ROYALE_ENABLED \? MAX_CONCURRENT_BR_MATCHES : 0,/,
    ""
  );

  result = result.replace(
    /brCountdownMin=\$\{resolveBattleRoyaleCountdownMinPlayers\(\)\}, maxConcurrentBR=\$\{MAX_CONCURRENT_BR_MATCHES\}, /,
    ""
  );

  result = result.replace(
    /\} else if \(isBattleRoyaleOnPlane\(ticket\)\) \{[\s\S]*?positionBranch = "br_on_plane";\s*\}/,
    ""
  );
  result = result.replace(
    /\} else if \(isBattleRoyaleParachuting\(ticket\)\) \{[\s\S]*?positionBranch = "br_parachute";\s*\}/,
    ""
  );
  result = result.replace(
    "const lockServerDeath = shouldLockBattleRoyaleDeathState(ticket, prevWasDead);",
    "const lockServerDeath = false;"
  );

  result = result.replace(/if \(!isBattleRoyaleCombatAllowed\(/g, "if (!isMatchCombatAllowed(");
  result = result.replace(
    /if \(!duelMatch\.isDuelSession\(matchesById\.get\(attackerTicket\.matchId\)\)\) \{\s*\n\s*recordBattleRoyaleKill\(attackerTicket\);\s*\n\s*\}/,
    ""
  );
  result = result.replace(/onBattleRoyalePlayerDied\([^)]+\);\s*/g, "");

  result = result.replace(
    /const session = ticket\.matchId \? matchesById\.get\(ticket\.matchId\) : null;\s*\n\s*const br = session && !duelMatch\.isDuelSession\(session\) \? ensureBattleRoyaleState\(session\) : null;\s*\n\s*const duel = session && duelMatch\.isDuelSession\(session\) \? duelMatch\.ensureDuelState\(session\) : null;\s*\n\s*if \(\(br && br\.joinLocked\) \|\| \(duel && duel\.joinLocked\)\) \{/,
    `const session = ticket.matchId ? matchesById.get(ticket.matchId) : null;
  const duel = session && duelMatch.isDuelSession(session) ? duelMatch.ensureDuelState(session) : null;
  if (duel && duel.joinLocked) {`
  );

  result = result.replace(
    /if \(duelMatch\.isDuelSession\(session\)\) \{\s*\n\s*duelMatch\.handleWsJoin\(session, ticketId\);\s*\n\s*sendMatchStateToSocket\(socket, session, ticketId\);\s*\n\s*\} else if \(br\) \{\s*[\s\S]*?sendMatchStateToSocket\(socket, session\);\s*\n\s*\}/,
    `if (duelMatch.isDuelSession(session)) {
        duelMatch.handleWsJoin(session, ticketId);
        sendMatchStateToSocket(socket, session, ticketId);
      }`
  );

  result = result.replace(
    /\} else \{\s*\n\s*const br = ensureBattleRoyaleState\(session\);\s*\n\s*if \(br && br\.joinLocked\) \{\s*\n\s*continue;\s*\n\s*\}\s*\n\s*if \(br && br\.phase !== "lobby" && br\.phase !== "countdown"\) \{\s*\n\s*continue;\s*\n\s*\}\s*\n\s*\}/,
    "}"
  );

  result = result.replace(
    /killCount: getBattleRoyaleKillCountForTicket\(ticket\.matchId, ticket\.ticketId\),/,
    "killCount: 0,"
  );

  result = result.replace(/\n\s*handleBattleRoyalePlayerLeft\(ticketId\);\s*\n/, "\n");

  result = result.replace(
    /(function recordDuelForfeitStats\(leaverTicketId\) \{)/,
    `${DUEL_HELPERS}\n$1`
  );

  return `${result.trim()}\n`;
}

const original = fs.readFileSync(serverPath, "utf8");
const refactored = refactor(original);
fs.writeFileSync(serverPath, refactored, "utf8");
console.log(`Refactored ${serverPath} (${original.length} -> ${refactored.length} bytes)`);
