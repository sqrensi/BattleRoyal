"use strict";

const { WEAPON_SLOT_EMPTY } = require("./player");

function mapPhaseToClient(serverPhase) {
  switch (serverPhase) {
    case "waiting":
      return "lobby";
    case "prep":
      return "prep";
    case "weapon_pick":
      return "round_pick";
    case "fight":
      return "round";
    case "round_end":
      return "round_end";
    case "match_end":
      return "ending";
    default:
      return "lobby";
  }
}

function buildMatchStateForTicket(duel, ticketId) {
  const nowMs = Date.now();
  const clientPhase = mapPhaseToClient(duel.phase);
  const opponent = duel.getOpponent(ticketId);
  const localPlayer = duel.players.find((p) => p.ticketId === ticketId) || null;

  let countdownRemainingSeconds = 0;
  if (duel.timerEndsAtMs > nowMs) {
    countdownRemainingSeconds = Math.max(0, Math.ceil((duel.timerEndsAtMs - nowMs) / 1000));
  }

  const localRoundWins = duel.roundWins[ticketId] || 0;
  const opponentRoundWins = opponent ? (duel.roundWins[opponent.ticketId] || 0) : 0;
  const teamIndex = typeof duel.getTeamIndex === "function"
    ? duel.getTeamIndex(ticketId)
    : duel.players.findIndex((p) => p.ticketId === ticketId);
  const spawnSlotIndex = typeof duel.getSpawnSlot === "function"
    ? duel.getSpawnSlot(ticketId)
    : 0;
  const opponentTeamIndex = opponent
    ? (typeof duel.getTeamIndex === "function"
      ? duel.getTeamIndex(opponent.ticketId)
      : (teamIndex >= 0 ? 1 - teamIndex : -1))
    : -1;
  const opponentSpawnSlotIndex = opponent && typeof duel.getSpawnSlot === "function"
    ? duel.getSpawnSlot(opponent.ticketId)
    : -1;
  const movementLocked = clientPhase === "ending" || clientPhase === "round_pick";
  const isAlive = !!(localPlayer && localPlayer.alive);
  const hasWeapon = !!(localPlayer && localPlayer.hasWeapon && localPlayer.weaponKind !== null && localPlayer.weaponKind >= 0);
  const combatEnabled = clientPhase === "round" && isAlive && hasWeapon;
  const pickedWeaponKind = hasWeapon ? localPlayer.weaponKind : -1;
  const isLocalWinner = !!(
    ticketId &&
    duel.winnerTicketId &&
    ticketId === duel.winnerTicketId &&
    clientPhase === "ending"
  );
  const connectedCount = duel.players.filter((p) => p.connected).length;

  return {
    type: "match_state",
    matchMode: "duel",
    phase: clientPhase,
    joinLocked: duel.phase !== "waiting",
    countdownRemainingSeconds,
    planeStartedAtMs: 0,
    planeEndsAtMs: 0,
    playingStartedAtMs: duel.phase === "fight" ? duel.timerEndsAtMs - duel.limits.roundTimeoutMs : 0,
    endingStartedAtMs: clientPhase === "ending" ? nowMs : 0,
    winnerTicketId: clientPhase === "round_end" || clientPhase === "ending"
      ? (duel.winnerTicketId || "")
      : "",
    aliveCount: connectedCount,
    connectedCount,
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
    planeSpawnIndex: teamIndex >= 0 ? teamIndex : 0,
    planeSpawnSlotCount: spawnSlotIndex >= 0 ? spawnSlotIndex : 0,
    localTicketId: ticketId || "",
    duelRoundNumber: duel.round || 0,
    duelLocalRoundWins: localRoundWins,
    duelOpponentRoundWins: opponentRoundWins,
    duelRoundsToWin: duel.roundsToWin,
    duelMovementLocked: movementLocked,
    duelCombatEnabled: combatEnabled,
    duelPickedWeaponKind: pickedWeaponKind,
    duelTeamIndex: teamIndex >= 0 ? teamIndex : -1,
    duelSpawnSlotIndex: spawnSlotIndex >= 0 ? spawnSlotIndex : -1,
    duelOpponentTeamIndex: opponentTeamIndex,
    duelOpponentSpawnSlotIndex: opponentSpawnSlotIndex,
    duelLocalDuelRating: localPlayer && Number.isFinite(localPlayer.duelRating)
      ? Math.max(0, localPlayer.duelRating)
      : 1000,
    duelOpponentNickname: opponent ? (opponent.nickname || opponent.playerId || "") : "",
    duelOpponentDuelRating: opponent && Number.isFinite(opponent.duelRating)
      ? Math.max(0, opponent.duelRating)
      : 1000,
  };
}

function duelHolstered(player) {
  if (player.hasWeapon && player.weaponKind !== null && player.weaponKind >= 0) {
    return !!player.isHolstered;
  }
  return player.weaponKind === null || player.weaponKind < 0;
}

function buildRemotePlayerState(player, serverTick, historySamples = 12) {
  const hasWeapon = !!(player.hasWeapon && player.weaponKind !== null && player.weaponKind >= 0);
  const weaponKind = hasWeapon ? player.weaponKind : 0;
  const sampleTick = player.poseSampleSeq > 0 ? player.poseSampleSeq : (serverTick || 0);
  const history = typeof player.getBroadcastStateHistory === "function"
    ? player.getBroadcastStateHistory(historySamples)
    : [];

  return {
    ticketId: player.ticketId,
    nickname: player.nickname || player.playerId || "Игрок",
    duelRating: Number.isFinite(player.duelRating) ? Math.max(0, player.duelRating) : 1000,
    position: { x: player.x, y: player.y, z: player.z },
    yaw: player.yaw || 0,
    lookPitch: player.lookPitch || 0,
    characterModel: player.characterModel || "",
    skinShirt: player.skinShirt || "",
    skinPants: player.skinPants || "",
    skinBoots: player.skinBoots || "",
    skinGloves: player.skinGloves || "",
    skinFace: player.skinFace || "",
    skinHair: player.skinHair || "",
    skinWeaponAssault: player.skinWeaponAssault || "",
    skinWeaponSniper: player.skinWeaponSniper || "",
    skinWeaponPistol: player.skinWeaponPistol || "",
    skinWeaponMp7: player.skinWeaponMp7 || "",
    isDead: !player.alive,
    hasWeapon,
    weaponKind,
    weaponSlot0Kind: Number.isFinite(player.weaponSlot0Kind) ? player.weaponSlot0Kind : WEAPON_SLOT_EMPTY,
    weaponSlot1Kind: Number.isFinite(player.weaponSlot1Kind) ? player.weaponSlot1Kind : WEAPON_SLOT_EMPTY,
    activeWeaponSlot: Number.isFinite(player.activeWeaponSlot) ? player.activeWeaponSlot : WEAPON_SLOT_EMPTY,
    isHolstered: duelHolstered(player),
    shotSeq: player.shotSeq || 0,
    reloadSeq: player.reloadSeq || 0,
    hitPlayerSeq: player.hitPlayerSeq || 0,
    footstepSeq: player.footstepSeq || 0,
    isCrouching: !!player.isCrouching,
    isSprinting: !!player.isSprinting,
    isSwimming: !!player.isSwimming,
    isAiming: !!player.isAiming,
    wallAvoidBlend: player.wallAvoidBlend || 0,
    deathSeq: player.deathSeq || 0,
    deathFallDirX: player.deathFallDirX || 0,
    deathFallDirY: player.deathFallDirY || 0,
    deathFallDirZ: player.deathFallDirZ || 0,
    animSpeed: player.animSpeed || 0,
    isGrounded: player.isGrounded !== false,
    jumpState: Number.isFinite(player.jumpState) ? player.jumpState : 0,
    animPhase: player.animPhase || 0,
    velX: player.velX || 0,
    velY: player.velY || 0,
    velZ: player.velZ || 0,
    moveInputX: player.moveInputX || 0,
    moveInputZ: player.moveInputZ || 0,
    sampleTick,
    sampleTimeMs: Date.now(),
    history,
    shotOriginX: player.shotOriginX || 0,
    shotOriginY: player.shotOriginY || 0,
    shotOriginZ: player.shotOriginZ || 0,
    shotDirX: player.shotDirX || 0,
    shotDirY: player.shotDirY || 0,
    shotDirZ: player.shotDirZ || 0,
    shotEndX: player.shotEndX || 0,
    shotEndY: player.shotEndY || 0,
    shotEndZ: player.shotEndZ || 0,
    shotHasEndPoint: !!player.shotHasEndPoint,
    recentShots: typeof player.formatRecentShotsForClient === "function" && player.shotSeq > 0
      ? player.formatRecentShotsForClient()
      : [],
    weaponPickupSeq: player.weaponPickupSeq || 0,
    medkitRemainingSeconds: player.medkitRemainingSeconds || 0,
    medkitCount: player.medkitCount || 0,
    isUsingMedkit: !!player.isUsingMedkit,
    killCount: player.roundWins || 0,
  };
}

function buildSnapshotForViewer(duel, viewerTicketId, tickRateHz, options) {
  const opts = options || {};
  const poseSampleRateHz = opts.poseSampleRateHz || tickRateHz;
  const historySamples = opts.snapshotHistorySamples || 12;
  const viewer = duel.players.find((p) => p.ticketId === viewerTicketId);
  const others = [];

  for (const player of duel.players) {
    if (player.ticketId === viewerTicketId) {
      continue;
    }
    // Duel / 1v1: always include opponent (spawn pose is authoritative before client poses arrive).
    const isSmallLobby = duel.mode === "duel" || duel.players.length <= 2;
    if (isSmallLobby || player.connected || player.hasPose) {
      others.push(buildRemotePlayerState(player, duel.serverTick, historySamples));
    }
  }

  const payload = {
    type: "snapshot",
    serverTick: duel.serverTick,
    serverTickRate: tickRateHz,
    movementSampleRateHz: poseSampleRateHz,
    players: others,
  };

  if (viewer && viewer.hasPose && duel.phase === "fight") {
    payload.selfAuthoritative = {
      position: { x: viewer.x, y: viewer.y, z: viewer.z },
      yaw: viewer.yaw || 0,
      sampleTick: viewer.poseSampleSeq > 0 ? viewer.poseSampleSeq : duel.serverTick,
    };
  }

  return payload;
}

module.exports = {
  mapPhaseToClient,
  buildMatchStateForTicket,
  buildSnapshotForViewer,
};
