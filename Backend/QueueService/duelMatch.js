"use strict";

const DUEL_PREP_SECONDS = Math.max(3, Number(process.env.DUEL_PREP_SECONDS) || 15);
const DUEL_ROUND_PICK_SECONDS = Math.max(3, Number(process.env.DUEL_ROUND_PICK_SECONDS) || 10);
const DUEL_ROUND_SECONDS = Math.max(5, Number(process.env.DUEL_ROUND_SECONDS) || 30);
const DUEL_ROUND_END_SECONDS = Math.max(1, Number(process.env.DUEL_ROUND_END_SECONDS) || 5);
const DUEL_ROUNDS_TO_WIN = Math.max(1, Number(process.env.DUEL_ROUNDS_TO_WIN) || 5);
const DUEL_MATCH_STATE_BROADCAST_MS = Math.max(100, Number(process.env.DUEL_MATCH_STATE_BROADCAST_MS) || 250);
const DUEL_WEAPON_SPARE_AMMO = 60;
const DUEL_WEAPON_KIND_MAX = 3;

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

function normalizeDuelWeaponKind(value) {
  const kind = Math.max(0, Math.min(DUEL_WEAPON_KIND_MAX, Number(value) || 0));
  return Number.isFinite(kind) ? kind : 0;
}

function resolveDuelWeaponItemId(kind) {
  const normalized = normalizeDuelWeaponKind(kind);
  if (normalized === 1) {
    return "sniper_rifle";
  }
  if (normalized === 2) {
    return "pistol";
  }
  if (normalized === 3) {
    return "mp7";
  }
  return "assault_rifle";
}

function clearDuelRoundWeapons(presence) {
  if (!presence) {
    return;
  }

  const emptySlot = Number.isFinite(deps.WEAPON_SLOT_EMPTY) ? deps.WEAPON_SLOT_EMPTY : 255;
  presence.weaponSlot0Kind = emptySlot;
  presence.weaponSlot1Kind = emptySlot;
  presence.weaponSlot0ItemId = "";
  presence.weaponSlot1ItemId = "";
  presence.weaponSlot0MagAmmo = -1;
  presence.weaponSlot1MagAmmo = -1;
  presence.activeWeaponSlot = emptySlot;
  presence.isHolstered = true;
  presence.hasWeapon = false;
  presence.weaponKind = 0;

  if (!Array.isArray(presence.spareAmmoByKind) || presence.spareAmmoByKind.length < 4) {
    presence.spareAmmoByKind = [0, 0, 0, 0];
  }
  presence.spareAmmoByKind[0] = 0;
  presence.spareAmmoByKind[1] = 0;
  presence.spareAmmoByKind[2] = 0;
  presence.spareAmmoByKind[3] = 0;

  if (typeof deps.syncWeaponPresenceFlags === "function") {
    deps.syncWeaponPresenceFlags(presence);
  }
}

function grantDuelWeaponToTicket(presence, weaponKind) {
  if (!presence) {
    return false;
  }

  const kind = normalizeDuelWeaponKind(weaponKind);
  const emptySlot = Number.isFinite(deps.WEAPON_SLOT_EMPTY) ? deps.WEAPON_SLOT_EMPTY : 255;
  const magSize = typeof deps.resolveMagazineSizeForKind === "function"
    ? deps.resolveMagazineSizeForKind(kind)
    : 30;
  const itemId = resolveDuelWeaponItemId(kind);

  clearDuelRoundWeapons(presence);

  presence.weaponSlot0Kind = kind;
  presence.weaponSlot0ItemId = itemId;
  presence.weaponSlot0MagAmmo = magSize;
  presence.activeWeaponSlot = 0;
  presence.isHolstered = false;
  presence.spareAmmoByKind[kind] = DUEL_WEAPON_SPARE_AMMO;
  presence.weaponPickupSeq = Math.max(0, Number(presence.weaponPickupSeq) || 0) + 1;

  if (typeof deps.syncWeaponPresenceFlags === "function") {
    deps.syncWeaponPresenceFlags(presence);
  } else {
    presence.hasWeapon = true;
    presence.weaponKind = kind;
  }

  return true;
}

function ensureDuelWeaponsBeforeFight(session) {
  if (!session) {
    return;
  }

  for (const ticketId of session.ticketIds) {
    const ticket = deps.ticketsById.get(ticketId);
    if (!ticket || !ticket.presence || ticket.presence.hasWeapon) {
      continue;
    }

    if (!grantDuelWeaponToTicket(ticket.presence, 0)) {
      continue;
    }

    if (typeof deps.sendPickupResultToSocket !== "function") {
      continue;
    }

    const socket = typeof deps.getTicketSocket === "function"
      ? deps.getTicketSocket(ticketId)
      : null;
    if (!socket) {
      continue;
    }

    const kind = normalizeDuelWeaponKind(ticket.presence.weaponKind);
    const itemId = resolveDuelWeaponItemId(kind);
    const magAmmo = Number.isFinite(ticket.presence.weaponSlot0MagAmmo)
      ? ticket.presence.weaponSlot0MagAmmo
      : (typeof deps.resolveMagazineSizeForKind === "function" ? deps.resolveMagazineSizeForKind(kind) : 30);
    const loadoutPayload = typeof deps.buildWeaponLoadoutPayload === "function"
      ? deps.buildWeaponLoadoutPayload(ticket.presence)
      : {};
    const sparePayload = typeof deps.buildSpareAmmoPayload === "function"
      ? deps.buildSpareAmmoPayload(ticket.presence)
      : {};

    deps.sendPickupResultToSocket(socket, true, "ok", {
      ticketId,
      weaponPickupSeq: ticket.presence.weaponPickupSeq,
      pickupKind: "weapon",
      itemId,
      weaponId: itemId,
      amount: 1,
      magAmmo,
      reserveAmmo: typeof deps.getSpareAmmo === "function" ? deps.getSpareAmmo(ticket.presence) : DUEL_WEAPON_SPARE_AMMO,
      ...sparePayload,
      ...loadoutPayload,
    });
  }
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
  ticket.poseHistory = [];
  clearDuelRoundWeapons(presence);
  presence.weaponPickupSeq = Math.max(0, Number(presence.weaponPickupSeq) || 0) + 1;
}

function isDuelRoundPresencePhase(ticket) {
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

  return duel.phase === "round_pick" || duel.phase === "round" || duel.phase === "round_end";
}

function shouldIgnoreClientDeathFlag(ticket, clientIsDead) {
  if (!clientIsDead) {
    return false;
  }

  if (!isDuelRoundPresencePhase(ticket)) {
    return false;
  }

  return !ticket.deathCause;
}

function canReviveForDuelRound(ticket) {
  if (!ticket || !ticket.matchId || ticket.deathCause) {
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

  return duel.phase === "round_pick" || duel.phase === "round";
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

  duel.phase = "round_pick";
  duel.phaseEndsAtMs = nowMs + (DUEL_ROUND_PICK_SECONDS * 1000);
  duel.roundResolved = false;
  duel.winnerTicketId = "";
  deps.broadcastMatchState(session.matchId);
  deps.broadcastMatchSnapshots(session.matchId);
}

function startDuelRoundFight(session, nowMs) {
  const duel = ensureDuelState(session);
  if (!duel) {
    return;
  }

  ensureDuelWeaponsBeforeFight(session);

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

function tryPickDuelWeapon(ticket, weaponKind) {
  if (!ticket || !ticket.matchId) {
    return { ok: false, reason: "no_ticket" };
  }

  const session = deps.matchesById.get(ticket.matchId);
  if (!isDuelSession(session)) {
    return { ok: false, reason: "not_duel" };
  }

  const duel = ensureDuelState(session);
  if (!duel || duel.phase !== "round_pick") {
    return { ok: false, reason: "pick_closed" };
  }

  if (!ticket.presence) {
    ticket.presence = deps.createDefaultPresence(
      typeof deps.getCurrentServerTick === "function" ? deps.getCurrentServerTick() : 0,
      Date.now()
    );
  }

  if (ticket.presence.hasWeapon) {
    return { ok: false, reason: "already_picked" };
  }

  const kind = normalizeDuelWeaponKind(weaponKind);
  if (!grantDuelWeaponToTicket(ticket.presence, kind)) {
    return { ok: false, reason: "grant_failed" };
  }

  const itemId = resolveDuelWeaponItemId(kind);
  const magAmmo = Number.isFinite(ticket.presence.weaponSlot0MagAmmo)
    ? ticket.presence.weaponSlot0MagAmmo
    : (typeof deps.resolveMagazineSizeForKind === "function" ? deps.resolveMagazineSizeForKind(kind) : 30);
  const loadoutPayload = typeof deps.buildWeaponLoadoutPayload === "function"
    ? deps.buildWeaponLoadoutPayload(ticket.presence)
    : {};
  const sparePayload = typeof deps.buildSpareAmmoPayload === "function"
    ? deps.buildSpareAmmoPayload(ticket.presence)
    : {};

  return {
    ok: true,
    reason: "ok",
    details: {
      ticketId: ticket.ticketId,
      weaponPickupSeq: ticket.presence.weaponPickupSeq,
      pickupKind: "weapon",
      itemId,
      weaponId: itemId,
      amount: 1,
      magAmmo,
      reserveAmmo: typeof deps.getSpareAmmo === "function" ? deps.getSpareAmmo(ticket.presence) : DUEL_WEAPON_SPARE_AMMO,
      ...sparePayload,
      ...loadoutPayload,
    },
  };
}

function handleWsWeaponPick(socket, message) {
  if (!socket || !message) {
    return false;
  }

  const ticket = typeof deps.resolveTicketFromSocket === "function"
    ? deps.resolveTicketFromSocket(socket)
    : null;
  if (!ticket) {
    return false;
  }

  const result = tryPickDuelWeapon(ticket, message.weaponKind);
  if (typeof deps.sendPickupResultToSocket === "function") {
    deps.sendPickupResultToSocket(
      socket,
      !!result.ok,
      result.reason || "",
      result.details || {}
    );
  }

  if (result.ok && ticket.matchId) {
    deps.broadcastMatchSnapshots(ticket.matchId);
    deps.broadcastMatchState(ticket.matchId);
  }

  return !!result.ok;
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

  return duel.phase === "round" || duel.phase === "round_end" || duel.phase === "round_pick";
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

  return duel.phase === "ending" || duel.phase === "round_pick";
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

  return duel.phase === "round" || duel.phase === "round_end" || duel.phase === "round_pick";
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
  if (!duel || duel.phase !== "round") {
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
  const movementLocked = duel.phase === "ending" || duel.phase === "round_pick";
  const isAlive = !!(ticketPresence && !ticketPresence.isDead);
  const hasWeapon = !!(ticketPresence && ticketPresence.hasWeapon);
  const combatEnabled = (duel.phase === "round" || duel.phase === "round_end") && isAlive && hasWeapon;
  const pickedWeaponKind = ticketPresence && ticketPresence.hasWeapon
    ? normalizeDuelWeaponKind(ticketPresence.weaponKind)
    : -1;
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
    duelPickedWeaponKind: pickedWeaponKind,
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

    if (duel.phase === "round_pick" && duel.phaseEndsAtMs > 0 && nowMs >= duel.phaseEndsAtMs) {
      startDuelRoundFight(session, nowMs);
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
  shouldIgnoreClientDeathFlag,
  canReviveForDuelRound,
  onDuelPlayerDied,
  buildDuelMatchStatePayload,
  tickDuelMatches,
  tryPickDuelWeapon,
  startDuelRoundFight,
  DUEL_ROUNDS_TO_WIN
};
