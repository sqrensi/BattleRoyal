"use strict";

const { formatNicknameWithPrefix } = require("../nicknamePrefix");

const Player = require("./player");
const { WEAPON_SLOT_EMPTY } = require("./player");
const combat = require("./combat");
const movement = require("./movement");
const { getSpawnCount, resolveSpawnPose, rollRandomSpawnSlot, rollUniqueSpawnSlots } = require("./dmSpawnTable");
const {
  buildMatchStateForTicket,
  buildSnapshotForViewer,
} = require("./clientProtocol");

const DM_RESPAWN_DEBUG =
  process.env.DM_RESPAWN_DEBUG === "1" || process.env.DM_RESPAWN_DEBUG === "true";
const DM_RESPAWN_TRACE =
  process.env.DM_RESPAWN_TRACE === "1" || process.env.DM_RESPAWN_TRACE === "true" || DM_RESPAWN_DEBUG;
const RESPAWN_POSE_GRACE_MS = 5000;
const JOIN_POSE_GRACE_MS = 6000;

function logDmRespawn(event, payload) {
  if (!DM_RESPAWN_TRACE) {
    return;
  }
  console.log(`[DMRespawn] ${event}`, JSON.stringify(payload));
}

class Deathmatch {
  constructor(matchData, limits) {
    this.id = matchData.id;
    this.mode = matchData.mode || "deathmatch";
    this.limits = limits;
    this.maxPlayers = Math.max(2, Number(limits.dmMaxPlayers) || 4);
    this.matchDurationMs = Math.max(60000, Number(limits.dmMatchDurationMs) || 600000);
    this.respawnDelayMs = Math.max(1000, Number(limits.dmRespawnDelayMs) || 3000);
    this.minPlayers = Math.max(2, Number(limits.dmMinPlayers) || 2);

    this.spawnSlotByTicket = new Map();
    const sortedPlayers = [...matchData.players].sort((a, b) =>
      String(a.ticketId).localeCompare(String(b.ticketId))
    );

    this.players = sortedPlayers.map((playerData) => new Player({ ...playerData, spawnX: 0, spawnY: 0, spawnZ: 0 }));
    this.playerByTicket = new Map(this.players.map((player) => [player.ticketId, player]));
    this.killCount = {};
    for (const player of this.players) {
      this.killCount[player.ticketId] = 0;
      player.matchKills = 0;
      player.matchDeaths = 0;
      player.damageDealt = 0;
      player.respawnAtMs = 0;
      player.joinPoseGraceUntilMs = 0;
    }

    this.rollAllSpawns();
    this.applySpawnPositions();

    this.phase = "waiting";
    const joinTimeoutMs = Math.max(2000, Number(limits.matchJoinTimeoutMs) || 20000);
    this.timerEndsAtMs = Date.now() + joinTimeoutMs;
    this.winnerTicketId = "";
    this.roundWins = this.killCount;

    this.serverTick = 0;
    this.snapshotDirty = true;
    this.stateDirty = true;
    this.finished = false;
    this.lastSnapshotMs = 0;
    this.lastStateBroadcastMs = 0;
    this.outbox = [];
    this.killFeedSeq = 0;
  }

  getPlayer(ticketId) {
    return this.playerByTicket.get(ticketId) || null;
  }

  markStateDirty() {
    this.stateDirty = true;
  }

  markSnapshotDirty() {
    this.snapshotDirty = true;
  }

  getSpawnSlot(ticketId) {
    if (this.spawnSlotByTicket.has(ticketId)) {
      return this.spawnSlotByTicket.get(ticketId);
    }
    return 0;
  }

  collectOccupiedSpawnSlots(excludeTicketId = null) {
    const occupied = new Set();
    for (const player of this.players) {
      if (!player || !player.alive) {
        continue;
      }

      if (excludeTicketId && player.ticketId === excludeTicketId) {
        continue;
      }

      occupied.add(this.getSpawnSlot(player.ticketId));
    }

    return occupied;
  }

  rollSpawnForTicket(ticketId) {
    const spawnCount = getSpawnCount();
    if (spawnCount <= 0) {
      this.spawnSlotByTicket.set(ticketId, 0);
      return 0;
    }

    const occupied = this.collectOccupiedSpawnSlots(ticketId);
    const slot = rollRandomSpawnSlot(occupied);
    this.spawnSlotByTicket.set(ticketId, slot);
    return slot;
  }

  rollAllSpawns() {
    this.spawnSlotByTicket.clear();
    const slots = rollUniqueSpawnSlots(this.players.length);
    for (let i = 0; i < this.players.length; i++) {
      const player = this.players[i];
      const slot = slots[i] ?? rollRandomSpawnSlot();
      this.spawnSlotByTicket.set(player.ticketId, slot);
    }
  }

  applySpawnPositions() {
    for (const player of this.players) {
      const slot = this.getSpawnSlot(player.ticketId);
      const pose = resolveSpawnPose(slot);
      if (!pose) {
        continue;
      }

      player.x = pose.x;
      player.y = pose.y;
      player.z = pose.z;
      player.yaw = pose.yaw || 0;
      player.velX = 0;
      player.velY = 0;
      player.velZ = 0;
      player.hasPose = true;
      player.recordStateSample();
    }
  }

  connectedCount() {
    return this.players.filter((p) => p.connected).length;
  }

  canStartMatch() {
    return this.connectedCount() >= Math.min(this.minPlayers, this.players.length);
  }

  queueMessage(ticketId, message) {
    this.outbox.push({ ticketId, message });
  }

  flushOutbox() {
    const items = this.outbox;
    this.outbox = [];
    return items;
  }

  broadcastKillFeed(killer, victim) {
    if (!killer || !victim) {
      return;
    }

    this.killFeedSeq += 1;
    const payload = {
      type: "kill_feed",
      seq: this.killFeedSeq,
      killerTicketId: killer.ticketId,
      victimTicketId: victim.ticketId,
      killerNickname: formatNicknameWithPrefix(killer.nicknamePrefix, killer.nickname || killer.playerId || "Игрок"),
      victimNickname: formatNicknameWithPrefix(victim.nicknamePrefix, victim.nickname || victim.playerId || "Игрок"),
      weaponKind: Math.max(0, Math.min(3, Number(killer.weaponKind) || 0)),
      cause: "player",
    };

    for (const player of this.players) {
      this.queueMessage(player.ticketId, payload);
    }
  }

  beginPrep(nowMs) {
    for (const player of this.players) {
      combat.clearWeapon(player);
    }
    this.phase = "prep";
    this.timerEndsAtMs = nowMs + this.limits.prepTimeoutMs;
    this.markStateDirty();
    this.markSnapshotDirty();
  }

  startFight(nowMs) {
    for (const player of this.players) {
      combat.clearWeapon(player);
      player.hp = player.maxHp;
      player.alive = true;
      player.respawnAtMs = 0;
      player.velX = 0;
      player.velY = 0;
      player.velZ = 0;
      player.lastSeq = null;
    }
    this.rollAllSpawns();
    this.applySpawnPositions();
    this.phase = "fight";
    this.timerEndsAtMs = nowMs + this.matchDurationMs;
    this.markStateDirty();
    this.markSnapshotDirty();
  }

  onJoin(ticketId) {
    const player = this.getPlayer(ticketId);
    if (!player) {
      return;
    }
    player.connected = true;
    if (!player.hasPose) {
      const slot = this.rollSpawnForTicket(ticketId);
      const pose = resolveSpawnPose(slot);
      if (pose) {
        player.x = pose.x;
        player.y = pose.y;
        player.z = pose.z;
        player.yaw = pose.yaw || 0;
        player.velX = 0;
        player.velY = 0;
        player.velZ = 0;
        player.hasPose = true;
        player.recordStateSample();
      }
    }

    if (this.phase === "fight" && player.alive) {
      combat.clearWeapon(player);
    }

    if (!player.joinPoseGraceUntilMs || player.joinPoseGraceUntilMs < Date.now()) {
      player.joinPoseGraceUntilMs = Date.now() + JOIN_POSE_GRACE_MS;
    }

    if (this.phase === "waiting" && this.canStartMatch()) {
      this.beginPrep(Date.now());
    }
    this.markStateDirty();
    this.markSnapshotDirty();
  }

  addPlayers(playersData, nowMs = Date.now()) {
    if (this.finished || this.phase === "match_end" || !Array.isArray(playersData)) {
      return [];
    }

    const addedTicketIds = [];
    for (const data of playersData) {
      if (this.players.length >= this.maxPlayers) {
        break;
      }

      const ticketId = typeof data.ticketId === "string" ? data.ticketId.trim() : "";
      if (!ticketId || this.playerByTicket.has(ticketId)) {
        continue;
      }

      const player = new Player({ ...data, spawnX: 0, spawnY: 0, spawnZ: 0 });
      player.connected = false;
      this.players.push(player);
      this.playerByTicket.set(player.ticketId, player);
      this.killCount[player.ticketId] = 0;
      player.matchKills = 0;
      this.rollSpawnForTicket(player.ticketId);

      const pose = resolveSpawnPose(this.getSpawnSlot(player.ticketId));
      if (pose) {
        player.x = pose.x;
        player.y = pose.y;
        player.z = pose.z;
        player.yaw = pose.yaw || 0;
        player.velX = 0;
        player.velY = 0;
        player.velZ = 0;
        player.hasPose = true;
        player.recordStateSample();
      }

      player.alive = true;
      player.hp = player.maxHp;
      player.respawnAtMs = 0;
      player.matchDeaths = 0;
      player.damageDealt = 0;
      player.joinPoseGraceUntilMs = nowMs + JOIN_POSE_GRACE_MS;
      combat.clearWeapon(player);

      if (this.phase === "fight") {
        player.lastSeq = null;
      }

      addedTicketIds.push(player.ticketId);
    }

    if (addedTicketIds.length > 0) {
      this.markStateDirty();
      this.markSnapshotDirty();
      logDmRespawn("players.added", {
        matchId: this.id,
        phase: this.phase,
        added: addedTicketIds,
        playerCount: this.players.length,
        nowMs,
      });
    }

    return addedTicketIds;
  }

  onDisconnect(ticketId) {
    const player = this.getPlayer(ticketId);
    if (!player) {
      return;
    }
    player.connected = false;

    if (this.finished) {
      return;
    }

    const connected = this.players.filter((p) => p.connected);
    if (connected.length === 0) {
      this.winnerTicketId = this.resolveWinnerByKills();
      this.phase = "match_end";
      this.finished = true;
      this.markStateDirty();
    }
  }

  onPose(ticketId, message, nowMs) {
    const player = this.getPlayer(ticketId);
    if (!player || !player.alive) {
      return;
    }

    const payload = this.phase === "fight" ? (message || {}) : this.stripWeaponFromPose(message);
    if (this.phase === "fight") {
      movement.applyPose(player, payload, this.limits, nowMs);
    } else {
      movement.applyPresencePose(player, payload, nowMs);
    }
    this.markSnapshotDirty();
  }

  stripWeaponFromPose(message) {
    const payload = message && typeof message === "object" ? { ...message } : {};
    payload.weaponKind = -1;
    payload.weaponSlot0Kind = WEAPON_SLOT_EMPTY;
    payload.weaponSlot1Kind = WEAPON_SLOT_EMPTY;
    payload.activeWeaponSlot = WEAPON_SLOT_EMPTY;
    payload.isHolstered = true;
    return payload;
  }

  onWeaponPick(ticketId, message, nowMs) {
    if (this.phase !== "fight") {
      return;
    }

    const player = this.getPlayer(ticketId);
    if (!player || !player.alive) {
      return;
    }

    const kind = Number(message.weaponKind ?? message.weapon);
    if (!Number.isFinite(kind)) {
      return;
    }

    combat.equipDeathmatchWeapon(player, kind);
    this.markStateDirty();
    this.markSnapshotDirty();
  }

  onShot(ticketId, message, nowMs) {
    if (this.phase !== "fight") {
      return;
    }

    const shooter = this.getPlayer(ticketId);
    if (!shooter || !shooter.alive) {
      return;
    }

    if (shooter.recordShotEvent(message)) {
      this.markSnapshotDirty();
    }
  }

  onHit(ticketId, message, nowMs) {
    if (this.phase !== "fight") {
      return;
    }

    const shooter = this.getPlayer(ticketId);
    const targetId = typeof message.targetTicketId === "string" ? message.targetTicketId.trim() : "";
    const target = this.getPlayer(targetId);
    if (!shooter || !target || target.ticketId === shooter.ticketId || !target.alive) {
      return;
    }

    const fire = combat.canFire(shooter, nowMs);
    if (!fire.ok) {
      return;
    }

    const hit = combat.validateHit(shooter, target, message, this.limits, nowMs);
    if (!hit.ok) {
      return;
    }

    const damage = combat.resolveHitDamage(message, fire.weapon);
    if (damage <= 0) {
      return;
    }

    shooter.damageDealt = Math.max(0, Number(shooter.damageDealt) || 0) + damage;

    let dirX = Number(message.dirX ?? 0);
    let dirY = Number(message.dirY ?? 0);
    let dirZ = Number(message.dirZ ?? 0);
    const dirMag = Math.sqrt(dirX * dirX + dirY * dirY + dirZ * dirZ);
    if (dirMag > 0.0001) {
      dirX /= dirMag;
      dirY /= dirMag;
      dirZ /= dirMag;
    } else {
      dirX = 0;
      dirY = 0;
      dirZ = 1;
    }

    const killed = combat.applyHit(shooter, target, damage, nowMs);
    if (killed) {
      target.matchDeaths = Math.max(0, Number(target.matchDeaths) || 0) + 1;
      target.deathSeq = Math.max(0, Number(target.deathSeq) || 0) + 1;
      logDmRespawn("kill", {
        ticketId: target.ticketId,
        deathSeq: target.deathSeq,
        position: { x: target.x, y: target.y, z: target.z },
        killerTicketId: shooter.ticketId,
      });
    }

    this.queueMessage(target.ticketId, {
      type: "damage",
      attackerTicketId: shooter.ticketId,
      targetTicketId: target.ticketId,
      damage,
      remainingHealth: target.hp,
      dirX,
      dirY,
      dirZ,
      killed,
      deathSeq: killed ? target.deathSeq : 0,
    });

    if (killed) {
      this.killCount[shooter.ticketId] = (this.killCount[shooter.ticketId] || 0) + 1;
      shooter.matchKills = this.killCount[shooter.ticketId];
      target.respawnAtMs = nowMs + this.respawnDelayMs;
      this.broadcastKillFeed(shooter, target);
      this.markStateDirty();
      this.markSnapshotDirty();
    }
  }

  respawnPlayer(player, nowMs) {
    if (!player || player.alive) {
      return;
    }

    const savedKind = player.weaponKind;
    const slot = this.rollSpawnForTicket(player.ticketId);
    const pose = resolveSpawnPose(slot);
    player.hp = player.maxHp;
    player.alive = true;
    player.respawnAtMs = 0;
    player.velX = 0;
    player.velY = 0;
    player.velZ = 0;
    player.poseSampleSeq = 0;
    player.stateHistory = [];
    player.lastSeq = null;
    player.lastPoseMs = nowMs;
    player.respawnPoseGraceUntilMs = nowMs + RESPAWN_POSE_GRACE_MS;
    if (pose) {
      player.x = pose.x;
      player.y = pose.y;
      player.z = pose.z;
      player.yaw = pose.yaw || 0;
      player.hasPose = true;
      player.recordStateSample();
    }
    if (savedKind !== null && savedKind >= 0) {
      combat.equipDeathmatchWeapon(player, savedKind);
    }
    player.deathSeq = Math.max(0, Number(player.deathSeq) || 0) + 1;
    logDmRespawn("respawn", {
      ticketId: player.ticketId,
      deathSeq: player.deathSeq,
      spawnSlot: slot,
      position: pose ? { x: pose.x, y: pose.y, z: pose.z } : null,
      sampleTick: player.poseSampleSeq,
      historyLen: player.stateHistory.length,
    });
    const respawnPayload = {
      type: "respawn",
      ticketId: player.ticketId,
      spawnSlotIndex: slot,
      deathSeq: player.deathSeq,
    };
    for (const peer of this.players) {
      this.queueMessage(peer.ticketId, respawnPayload);
    }
    this.markStateDirty();
    this.markSnapshotDirty();
  }

  processRespawns(nowMs) {
    if (this.phase !== "fight") {
      return;
    }

    for (const player of this.players) {
      if (player.alive || !player.respawnAtMs) {
        continue;
      }
      if (nowMs >= player.respawnAtMs) {
        this.respawnPlayer(player, nowMs);
      }
    }
  }

  resolveWinnerByKills() {
    let bestTicketId = "";
    let bestKills = -1;
    let tied = false;

    for (const player of this.players) {
      const kills = Math.max(0, Number(this.killCount[player.ticketId]) || 0);
      if (kills > bestKills) {
        bestKills = kills;
        bestTicketId = player.ticketId;
        tied = false;
      } else if (kills === bestKills && kills >= 0) {
        if (bestTicketId) {
          tied = true;
        }
      }
    }

    if (tied || !bestTicketId || bestKills <= 0) {
      return "";
    }
    return bestTicketId;
  }

  endMatch(nowMs) {
    this.winnerTicketId = this.resolveWinnerByKills();
    this.phase = "match_end";
    this.finished = true;
    this.timerEndsAtMs = nowMs;
    this.markStateDirty();
    this.markSnapshotDirty();
  }

  buildDeathCounts() {
    const deathCounts = {};
    for (const player of this.players) {
      deathCounts[player.ticketId] = Math.max(0, Number(player.matchDeaths) || 0);
    }
    return deathCounts;
  }

  tick(nowMs) {
    this.serverTick += 1;
    const dtSec = 1 / this.limits.tickRateHz;

    this.processRespawns(nowMs);

    if (nowMs >= this.timerEndsAtMs) {
      switch (this.phase) {
        case "waiting":
          if (this.canStartMatch()) {
            this.beginPrep(nowMs);
          } else {
            this.winnerTicketId = "";
            this.phase = "match_end";
            this.finished = true;
          }
          this.markStateDirty();
          break;
        case "prep":
          this.startFight(nowMs);
          break;
        case "fight":
          this.endMatch(nowMs);
          break;
        default:
          break;
      }
    }

    if (this.phase === "fight") {
      for (const player of this.players) {
        if (player.alive) {
          movement.tickMovement(player, dtSec);
        }
      }
    }
  }

  shouldSendSnapshot(nowMs, snapshotIntervalMs) {
    if (nowMs - this.lastSnapshotMs < snapshotIntervalMs) {
      return false;
    }
    return this.snapshotDirty ||
      this.phase === "fight" ||
      this.phase === "prep";
  }

  buildSnapshotForViewer(viewerTicketId) {
    return buildSnapshotForViewer(this, viewerTicketId, this.limits.tickRateHz, {
      poseSampleRateHz: this.limits.poseSampleRateHz,
      snapshotHistorySamples: this.limits.snapshotHistorySamples,
    });
  }

  buildStateForTicket(ticketId) {
    return buildMatchStateForTicket(this, ticketId);
  }
}

module.exports = Deathmatch;
