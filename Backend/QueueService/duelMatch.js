"use strict";

const DUEL_PREP_SECONDS = Math.max(3, Number(process.env.DUEL_PREP_SECONDS) || 15);
const DUEL_ROUND_SECONDS = Math.max(5, Number(process.env.DUEL_ROUND_SECONDS) || 30);
const DUEL_ROUND_END_SECONDS = Math.max(1, Number(process.env.DUEL_ROUND_END_SECONDS) || 5);
const DUEL_ROUNDS_TO_WIN = Math.max(1, Number(process.env.DUEL_ROUNDS_TO_WIN) || 5);
const DUEL_MATCH_STATE_BROADCAST_MS = Math.max(100, Number(process.env.DUEL_MATCH_STATE_BROADCAST_MS) || 250);

let deps = null;

function initDuelMatchModule(moduleDeps) {
  deps = moduleDeps;
}

function isDuelSession(session) {
  return !!session && normalizeMatchMode(session.matchMode) === "duel";
}

function normalizeMatchMode(value) {
  if (typeof deps.normalizeMatchMode === "function") {
    return deps.normalizeMatchMode(value);
  }

  const raw = String(value || "").trim().toLowerCase();
  if (raw === "duel" || raw === "1v1" || raw === "duel_1v1") {
    return "duel";
  }

  return raw || "battle_royale";
}

function createDuelState() {
  return {
    phase: "lobby",
    phaseEndsAtMs: 0,
    joinLocked: false,
    connectedTickets: new Set(),
    teamIndexByTicket: new Map(),
    roundWinsByTicket: new Map(),
    roundNumber: 0,
    spawnSlotIndexByTicket: new Map(),
    roundResolved: false,
    winnerTicketId: "",
    lastStateBroadcastMs: 0,
    endingStartedAtMs: 0
  };
}

function ensureDuelState(session) {
  if (!session) {
    return null;
  }

  if (!session.duel) {
    session.duel = createDuelState();
  }

  return session.duel;
}

function getDuelRoundWins(duel, ticketId) {
  if (!duel || !ticketId) {
    return 0;
  }

  return Math.max(0, duel.roundWinsByTicket.get(ticketId) || 0);
}

function resetTicketPresenceForDuelRound(ticket) {
  if (!ticket) {
    return;
  }

  if (!ticket.presence) {
    ticket.presence = deps.createDefaultPresence(
      typeof deps.getCurrentServerTick === "function" ? deps.getCurrentServerTick() : 0,
      Date.now()
    );
  }

  const presence = ticket.presence;
  ticket.deathCause = "";
  presence.health = Number.isFinite(presence.maxHealth) ? presence.maxHealth : 100;
  presence.maxHealth = Number.isFinite(presence.maxHealth) ? presence.maxHealth : 100;
  presence.isDead = false;
  presence.deathSeq = Math.max(0, Number(presence.deathSeq) || 0);
  presence.isUsingMedkit = false;
  presence.medkitUseEndsAtMs = 0;
}

function assignDuelTeams(session) {
  const duel = ensureDuelState(session);
  if (!duel || !session) {
    return;
  }

  const ticketIds = Array.from(session.ticketIds).sort();
  duel.teamIndexByTicket.clear();
  for (let i = 0; i < ticketIds.length; i++) {
    duel.teamIndexByTicket.set(ticketIds[i], i % 2);
    if (!duel.roundWinsByTicket.has(ticketIds[i])) {
      duel.roundWinsByTicket.set(ticketIds[i], 0);
    }
  }
}

function rollDuelSpawns(session) {
  const duel = ensureDuelState(session);
  if (!duel || !session) {
    return;
  }

  const spawnCounts = [4, 4];
  duel.spawnSlotIndexByTicket.clear();
  for (const ticketId of session.ticketIds) {
    const teamIndex = duel.teamIndexByTicket.get(ticketId) || 0;
    const maxSlot = spawnCounts[teamIndex] || 1;
    const slot = Math.floor(Math.random() * maxSlot);
    duel.spawnSlotIndexByTicket.set(ticketId, slot);
  }
}

function maybeStartDuelMatch(session) {
  const duel = ensureDuelState(session);
  if (!duel || duel.phase !== "lobby") {
    return;
  }

  if (duel.connectedTickets.size < 2) {
    return;
  }

  assignDuelTeams(session);
  startDuelPrep(session, Date.now());
}

function startDuelPrep(session, nowMs) {
  const duel = ensureDuelState(session);
  if (!duel) {
    return;
  }

  duel.phase = "prep";
  duel.joinLocked = true;
  duel.phaseEndsAtMs = nowMs + (DUEL_PREP_SECONDS * 1000);
  duel.roundNumber = 0;
  duel.roundResolved = false;
  duel.winnerTicketId = "";
  duel.spawnSlotIndexByTicket.clear();
  deps.broadcastMatchState(session.matchId);
}

function startDuelRound(session, nowMs) {
  const duel = ensureDuelState(session);
  if (!duel) {
    return;
  }

  if (duel.phase === "prep") {
    duel.roundNumber = 1;
  } else if (duel.phase === "round_end") {
    duel.roundNumber = Math.max(1, (duel.roundNumber || 0) + 1);
  } else if (duel.roundNumber <= 0) {
    duel.roundNumber = 1;
  }

  rollDuelSpawns(session);

  for (const ticketId of session.ticketIds) {
    const ticket = deps.ticketsById.get(ticketId);
    if (ticket) {
      resetTicketPresenceForDuelRound(ticket);
    }
  }

  duel.phase = "round";
  duel.phaseEndsAtMs = nowMs + (DUEL_ROUND_SECONDS * 1000);
  duel.roundResolved = false;
  duel.winnerTicketId = "";
  deps.broadcastMatchState(session.matchId);
  deps.broadcastMatchSnapshots(session.matchId);
}

function finishDuelRound(session, nowMs, winnerTicketId) {
  const duel = ensureDuelState(session);
  if (!duel || duel.roundResolved) {
    return;
  }

  duel.roundResolved = true;
  duel.winnerTicketId = winnerTicketId || "";

  if (winnerTicketId) {
    const current = getDuelRoundWins(duel, winnerTicketId);
    duel.roundWinsByTicket.set(winnerTicketId, current + 1);
  }

  deps.broadcastMatchState(session.matchId);

  const matchWinner = resolveDuelMatchWinner(session);
  if (matchWinner) {
    startDuelEnding(session, nowMs, matchWinner);
    return;
  }

  duel.phase = "round_end";
  duel.phaseEndsAtMs = nowMs + (DUEL_ROUND_END_SECONDS * 1000);
  deps.broadcastMatchState(session.matchId);
}

function resolveOpponentTicketId(session, ticketId) {
  for (const otherId of session.ticketIds) {
    if (otherId !== ticketId) {
      return otherId;
    }
  }

  return "";
}

function resolveDuelMatchWinner(session) {
  const duel = ensureDuelState(session);
  if (!duel) {
    return "";
  }

  for (const [ticketId, wins] of duel.roundWinsByTicket.entries()) {
    if (wins >= DUEL_ROUNDS_TO_WIN) {
      return ticketId;
    }
  }

  return "";
}

function startDuelEnding(session, nowMs, winnerTicketId) {
  const duel = ensureDuelState(session);
  if (!duel) {
    return;
  }

  duel.phase = "ending";
  duel.phaseEndsAtMs = 0;
  duel.endingStartedAtMs = nowMs;
  duel.winnerTicketId = winnerTicketId || "";
  duel.joinLocked = true;
  deps.broadcastMatchState(session.matchId);
}

function handleWsJoin(session, ticketId) {
  if (!isDuelSession(session)) {
    return false;
  }

  const duel = ensureDuelState(session);
  duel.connectedTickets.add(ticketId);
  assignDuelTeams(session);
  maybeStartDuelMatch(session);
  return true;
}

function handleWsWeaponPick() {
  // Weapon pick UI/grant removed from duel mode.
}

function shouldIgnoreClientWeaponPoseSync(ticket) {
  if (!ticket || !ticket.matchId) {
    return false;
  }

  const session = deps.matchesById.get(ticket.matchId);
  if (!isDuelSession(session)) {
    return false;
  }

  const duel = ensureDuelState(session);
  if (!duel) {
    return false;
  }

  return duel.phase === "round" || duel.phase === "round_end";
}

function resolveDuelSnapshotHolstered(ticket) {
  if (!ticket || !ticket.presence) {
    return true;
  }

  if (!shouldIgnoreClientWeaponPoseSync(ticket)) {
    return !!ticket.presence.isHolstered;
  }

  if (ticket.presence.hasWeapon) {
    return false;
  }

  return !!ticket.presence.isHolstered;
}

function handleDuelPlayerLeft(ticketId) {
  const ticket = deps.ticketsById.get(ticketId);
  if (!ticket || !ticket.matchId) {
    return null;
  }

  const session = deps.matchesById.get(ticket.matchId);
  if (!isDuelSession(session)) {
    return null;
  }

  const duel = ensureDuelState(session);
  if (!duel) {
    return null;
  }

  duel.connectedTickets.delete(ticketId);

  if (session.state === "Ended" || duel.phase === "ending" || duel.phase === "lobby") {
    deps.broadcastMatchState(session.matchId);
    return { forfeitTicketId: "" };
  }

  const remainingTicketIds = Array.from(duel.connectedTickets);
  if (remainingTicketIds.length === 1) {
    const winnerTicketId = remainingTicketIds[0];
    startDuelEnding(session, Date.now(), winnerTicketId);
    return { forfeitTicketId: ticketId, winnerTicketId };
  }

  deps.broadcastMatchState(session.matchId);
  return { forfeitTicketId: "" };
}

function isDuelMovementLocked(ticket) {
  if (!ticket || !ticket.matchId) {
    return false;
  }

  const session = deps.matchesById.get(ticket.matchId);
  if (!isDuelSession(session)) {
    return false;
  }

  const duel = ensureDuelState(session);
  if (!duel) {
    return false;
  }

  return duel.phase === "ending";
}

function isDuelRespawnBlocked(ticket) {
  if (!ticket || !ticket.matchId) {
    return false;
  }

  const session = deps.matchesById.get(ticket.matchId);
  if (!isDuelSession(session)) {
    return false;
  }

  const duel = ensureDuelState(session);
  if (!duel) {
    return false;
  }

  return duel.phase === "round" || duel.phase === "round_end";
}

function isDuelCombatAllowed(ticket) {
  if (!ticket || !ticket.matchId) {
    return true;
  }

  const session = deps.matchesById.get(ticket.matchId);
  if (!isDuelSession(session)) {
    return true;
  }

  const duel = ensureDuelState(session);
  if (!duel || (duel.phase !== "round" && duel.phase !== "round_end")) {
    return false;
  }

  if (!ticket.presence || ticket.presence.isDead) {
    return false;
  }

  return true;
}

function onDuelPlayerDied(victimTicket, killerTicketId) {
  if (!victimTicket || !victimTicket.matchId) {
    return false;
  }

  const session = deps.matchesById.get(victimTicket.matchId);
  if (!isDuelSession(session)) {
    return false;
  }

  const duel = ensureDuelState(session);
  if (!duel || duel.phase !== "round" || duel.roundResolved) {
    return true;
  }

  const killerTicketIdResolved = typeof killerTicketId === "string" && killerTicketId
    ? killerTicketId
    : resolveOpponentTicketId(session, victimTicket.ticketId);
  finishDuelRound(session, Date.now(), killerTicketIdResolved);
  return true;
}

function buildDuelMatchStatePayload(session, ticketId) {
  const duel = ensureDuelState(session);
  const nowMs = Date.now();
  if (!duel) {
    return {
      type: "match_state",
      matchMode: "duel",
      phase: "lobby",
      localTicketId: ticketId || ""
    };
  }

  let countdownRemainingSeconds = 0;
  if (duel.phaseEndsAtMs > nowMs) {
    countdownRemainingSeconds = Math.max(0, Math.ceil((duel.phaseEndsAtMs - nowMs) / 1000));
  }

  const opponentTicketId = resolveOpponentTicketId(session, ticketId);
  const localRoundWins = getDuelRoundWins(duel, ticketId);
  const opponentRoundWins = getDuelRoundWins(duel, opponentTicketId);
  const teamIndex = duel.teamIndexByTicket.get(ticketId);
  const spawnSlotIndex = duel.spawnSlotIndexByTicket.get(ticketId);
  const ticketPresence = ticketId ? deps.ticketsById.get(ticketId)?.presence : null;
  const movementLocked = duel.phase === "ending";
  const isAlive = !!(ticketPresence && !ticketPresence.isDead);
  const combatEnabled = (duel.phase === "round" || duel.phase === "round_end") && isAlive;
  const isLocalWinner = !!(ticketId && duel.winnerTicketId && ticketId === duel.winnerTicketId && duel.phase === "ending");

  return {
    type: "match_state",
    matchMode: "duel",
    phase: duel.phase,
    joinLocked: !!duel.joinLocked,
    countdownRemainingSeconds,
    planeStartedAtMs: 0,
    planeEndsAtMs: 0,
    playingStartedAtMs: duel.phase === "round" ? duel.phaseEndsAtMs - (DUEL_ROUND_SECONDS * 1000) : 0,
    endingStartedAtMs: duel.endingStartedAtMs || 0,
    winnerTicketId: duel.winnerTicketId || "",
    aliveCount: duel.connectedTickets.size,
    connectedCount: duel.connectedTickets.size,
    hasJumped: false,
    hasLanded: true,
    inCombat: combatEnabled,
    localKillCount: localRoundWins,
    isLocalWinner,
    winnerDisconnectSeconds: 0,
    forceJump: false,
    useForcedDrop: false,
    dropPosX: 0,
    dropPosZ: 0,
    mapCenterX: 0,
    mapCenterZ: 0,
    planeStartX: 0,
    planeStartZ: 0,
    planeEndX: 0,
    planeEndZ: 0,
    planeY: 0,
    planeSpeed: 0,
    planePosX: 0,
    planePosY: 0,
    planePosZ: 0,
    planeSpawnIndex: Number.isFinite(teamIndex) ? teamIndex : 0,
    planeSpawnSlotCount: Number.isFinite(spawnSlotIndex) ? spawnSlotIndex : 0,
    localTicketId: ticketId || "",
    duelRoundNumber: duel.roundNumber || 0,
    duelLocalRoundWins: localRoundWins,
    duelOpponentRoundWins: opponentRoundWins,
    duelRoundsToWin: DUEL_ROUNDS_TO_WIN,
    duelMovementLocked: movementLocked,
    duelCombatEnabled: combatEnabled,
    duelPickedWeaponKind: -1,
    duelTeamIndex: Number.isFinite(teamIndex) ? teamIndex : -1,
    duelSpawnSlotIndex: Number.isFinite(spawnSlotIndex) ? spawnSlotIndex : -1
  };
}

function tickDuelMatches(nowMs) {
  for (const session of deps.matchesById.values()) {
    if (!session || session.state === "Ended" || !isDuelSession(session)) {
      continue;
    }

    const duel = ensureDuelState(session);
    if (!duel) {
      continue;
    }

    if (duel.phase === "prep" && duel.phaseEndsAtMs > 0 && nowMs >= duel.phaseEndsAtMs) {
      startDuelRound(session, nowMs);
      continue;
    }

    if (duel.phase === "round" && duel.phaseEndsAtMs > 0 && nowMs >= duel.phaseEndsAtMs && !duel.roundResolved) {
      finishDuelRound(session, nowMs, "");
      continue;
    }

    if (duel.phase === "round_end" && duel.phaseEndsAtMs > 0 && nowMs >= duel.phaseEndsAtMs) {
      const matchWinner = resolveDuelMatchWinner(session);
      if (matchWinner) {
        startDuelEnding(session, nowMs, matchWinner);
      } else {
        startDuelRound(session, nowMs);
      }

      continue;
    }

    if (nowMs - (duel.lastStateBroadcastMs || 0) >= DUEL_MATCH_STATE_BROADCAST_MS) {
      duel.lastStateBroadcastMs = nowMs;
      deps.broadcastMatchState(session.matchId);
    }
  }
}

module.exports = {
  initDuelMatchModule,
  isDuelSession,
  createDuelState,
  ensureDuelState,
  handleWsJoin,
  handleWsWeaponPick,
  handleDuelPlayerLeft,
  shouldIgnoreClientWeaponPoseSync,
  resolveDuelSnapshotHolstered,
  isDuelCombatAllowed,
  isDuelMovementLocked,
  isDuelRespawnBlocked,
  onDuelPlayerDied,
  buildDuelMatchStatePayload,
  tickDuelMatches,
  DUEL_ROUNDS_TO_WIN
};
