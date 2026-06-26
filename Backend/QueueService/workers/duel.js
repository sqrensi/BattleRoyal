"use strict";

const Player = require("./player");
const { WEAPON_SLOT_EMPTY } = require("./player");
const combat = require("./combat");
const movement = require("./movement");
const DuelAiController = require("./duelAiController");
const { getSpawnCount, resolveSpawnPose } = require("./spawnTable");
const {
  buildMatchStateForTicket,
  buildSnapshotForViewer,
} = require("./clientProtocol");

class Duel {
  constructor(matchData, limits) {
    this.id = matchData.id;
    this.mode = matchData.mode || "duel";
    this.limits = limits;
    this.roundsToWin = limits.roundsToWin;

    this.aiController = new DuelAiController(this, limits);
    this.teamIndexByTicket = new Map();
    this.spawnSlotByTicket = new Map();

    const sortedPlayers = [...matchData.players].sort((a, b) =>
      String(a.ticketId).localeCompare(String(b.ticketId))
    );

    this.players = sortedPlayers.map((playerData, index) => {
      const teamIndex = index % 2;
      this.teamIndexByTicket.set(playerData.ticketId, teamIndex);
      const player = new Player({ ...playerData, spawnX: 0, spawnY: 0, spawnZ: 0 });
      if (playerData.isAiBot) {
        this.aiController.initializeBot(player, playerData.aiProfile || {}, null);
      }
      return player;
    });

    this.rollRoundSpawns();
    this.applySpawnPositions();

    this.phase = "waiting";
    this.round = 0;
    const joinTimeoutMs = Math.max(5000, Number(limits.matchJoinTimeoutMs) || 20000);
    this.timerEndsAtMs = Date.now() + joinTimeoutMs;
    this.winnerTicketId = "";
    this.roundWins = {};
    for (const p of this.players) {
      this.roundWins[p.ticketId] = 0;
    }

    this.serverTick = 0;
    this.dirty = true;
    this.finished = false;
    this.lastSnapshotMs = 0;
    this.outbox = [];
    this.killFeedSeq = 0;
  }

  getTeamIndex(ticketId) {
    if (this.teamIndexByTicket.has(ticketId)) {
      return this.teamIndexByTicket.get(ticketId);
    }
    return this.players.findIndex((p) => p.ticketId === ticketId);
  }

  getSpawnSlot(ticketId) {
    if (this.spawnSlotByTicket.has(ticketId)) {
      return this.spawnSlotByTicket.get(ticketId);
    }
    return 0;
  }

  rollRoundSpawns() {
    this.spawnSlotByTicket.clear();
    for (const player of this.players) {
      const teamIndex = Math.max(0, this.getTeamIndex(player.ticketId));
      const slotCount = Math.max(1, getSpawnCount(teamIndex));
      const slot = Math.floor(Math.random() * slotCount);
      this.spawnSlotByTicket.set(player.ticketId, slot);
    }
  }

  applySpawnPositions() {
    for (const player of this.players) {
      const teamIndex = Math.max(0, this.getTeamIndex(player.ticketId));
      const slot = this.getSpawnSlot(player.ticketId);
      const pose = resolveSpawnPose(teamIndex, slot);
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
    }
    this.aiController.onSpawnPositionsApplied();
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
      killerNickname: killer.nickname || killer.playerId || "Игрок",
      victimNickname: victim.nickname || victim.playerId || "Игрок",
      weaponKind: Math.max(0, Math.min(3, Number(killer.weaponKind) || 0)),
      cause: "player",
    };

    for (const player of this.players) {
      this.queueMessage(player.ticketId, payload);
    }
  }

  getOpponent(ticketId) {
    return this.players.find((p) => p.ticketId !== ticketId) || null;
  }

  bothConnected() {
    return this.players.every((p) => p.isAiBot || p.connected);
  }

  hasHumanConnected() {
    return this.players.some((p) => !p.isAiBot && p.connected);
  }

  canStartMatch() {
    if (!this.bothConnected()) {
      return false;
    }
    return this.hasHumanConnected() || this.players.every((p) => p.isAiBot);
  }

  queueMessage(ticketId, message) {
    this.outbox.push({ ticketId, message });
  }

  flushOutbox() {
    const items = this.outbox;
    this.outbox = [];
    return items;
  }

  beginPrep(nowMs) {
    this.phase = "prep";
    this.round = 0;
    this.winnerTicketId = "";
    this.timerEndsAtMs = nowMs + this.limits.prepTimeoutMs;
    this.aiController.onPhaseChange("prep");
    this.dirty = true;
  }

  beginWeaponPick(nowMs) {
    if (this.round <= 0) {
      this.round = 1;
    }
    this.phase = "weapon_pick";
    this.winnerTicketId = "";
    this.rollRoundSpawns();
    this.applySpawnPositions();
    for (const p of this.players) {
      p.weaponKind = null;
      p.hasWeapon = false;
      p.weaponSlot0Kind = WEAPON_SLOT_EMPTY;
      p.weaponSlot1Kind = WEAPON_SLOT_EMPTY;
      p.activeWeaponSlot = WEAPON_SLOT_EMPTY;
      p.isHolstered = true;
    }
    this.timerEndsAtMs = nowMs + this.limits.weaponPickTimeoutMs;
    this.aiController.onPhaseChange("weapon_pick");
    this.dirty = true;
  }

  onJoin(ticketId) {
    const player = this.players.find((p) => p.ticketId === ticketId);
    if (!player) {
      return;
    }
    player.connected = true;

    if (this.phase === "waiting" && this.canStartMatch()) {
      this.beginPrep(Date.now());
    }
  }

  onDisconnect(ticketId) {
    const player = this.players.find((p) => p.ticketId === ticketId);
    if (!player) {
      return;
    }
    player.connected = false;

    if (this.finished) {
      return;
    }

    const opponent = this.getOpponent(ticketId);
    if (opponent) {
      this.winnerTicketId = opponent.ticketId;
      this.phase = "match_end";
      this.finished = true;
      this.dirty = true;
    }
  }

  onPose(ticketId, message, nowMs) {
    const player = this.players.find((p) => p.ticketId === ticketId);
    if (!player || !player.alive) {
      return;
    }

    if (this.phase === "fight") {
      movement.applyPose(player, message, this.limits, nowMs);
    } else {
      movement.applyPresencePose(player, message, nowMs);
    }
    this.dirty = true;
  }

  onWeaponPick(ticketId, message, nowMs) {
    if (this.phase !== "weapon_pick") {
      return;
    }
    const player = this.players.find((p) => p.ticketId === ticketId);
    if (!player) {
      return;
    }
    if (player.hasWeapon && player.weaponKind !== null) {
      return;
    }
    const kind = Number(message.weaponKind ?? message.weapon);
    if (!Number.isFinite(kind)) {
      return;
    }
    combat.equipWeapon(player, kind);
    this.dirty = true;
  }

  onShot(ticketId, message, nowMs) {
    if (this.phase !== "fight") {
      return;
    }

    const shooter = this.players.find((p) => p.ticketId === ticketId);
    if (!shooter) {
      return;
    }

    if (shooter.recordShotEvent(message)) {
      this.dirty = true;
    }
  }

  onHit(ticketId, message, nowMs) {
    if (this.phase !== "fight") {
      return;
    }

    const shooter = this.players.find((p) => p.ticketId === ticketId);
    const targetId = typeof message.targetTicketId === "string" ? message.targetTicketId.trim() : "";
    const target = this.players.find((p) => p.ticketId === targetId) || this.getOpponent(ticketId);
    if (!shooter || !target || target.ticketId === shooter.ticketId || !target.alive) {
      return;
    }

    const fire = combat.canFire(shooter, nowMs);
    if (!fire.ok) {
      return;
    }

    const hit = combat.validateHit(shooter, target, message, this.limits);
    if (!hit.ok) {
      return;
    }

    const damage = combat.resolveHitDamage(message, fire.weapon);
    if (damage <= 0) {
      return;
    }

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
    this.queueMessage(target.ticketId, {
      type: "damage",
      attackerTicketId: shooter.ticketId,
      targetTicketId: target.ticketId,
      damage,
      remainingHealth: target.hp,
      dirX,
      dirY,
      dirZ,
    });

    if (killed) {
      target.deathSeq = Math.max(0, Number(target.deathSeq) || 0) + 1;
      this.broadcastKillFeed(shooter, target);
      this.roundWins[shooter.ticketId] = (this.roundWins[shooter.ticketId] || 0) + 1;
      shooter.roundWins = this.roundWins[shooter.ticketId];
      this.winnerTicketId = shooter.ticketId;
      this.phase = "round_end";
      this.timerEndsAtMs = nowMs + this.limits.roundEndTimeoutMs;
      this.dirty = true;
    }
  }

  startFight(nowMs) {
    this.applySpawnPositions();
    for (let i = 0; i < this.players.length; i++) {
      const savedKind = this.players[i].weaponKind;
      const spawnPose = resolveSpawnPose(
        Math.max(0, this.getTeamIndex(this.players[i].ticketId)),
        this.getSpawnSlot(this.players[i].ticketId)
      );
      this.players[i].resetRound(spawnPose);
      if (savedKind !== null && savedKind >= 0) {
        combat.equipWeapon(this.players[i], savedKind);
      }
    }
    this.phase = "fight";
    this.timerEndsAtMs = nowMs + this.limits.roundTimeoutMs;
    this.aiController.onPhaseChange("fight");
    this.dirty = true;
  }

  awardRoundWin(ticketId, nowMs) {
    this.roundWins[ticketId] = (this.roundWins[ticketId] || 0) + 1;
    const player = this.players.find((p) => p.ticketId === ticketId);
    if (player) {
      player.roundWins = this.roundWins[ticketId];
    }
    this.winnerTicketId = ticketId;
    this.phase = "round_end";
    this.timerEndsAtMs = nowMs + this.limits.roundEndTimeoutMs;
    this.dirty = true;
  }

  resolveRoundTimeout(nowMs) {
    const alive = this.players.filter((p) => p.alive);
    if (alive.length === 1) {
      this.awardRoundWin(alive[0].ticketId, nowMs);
      return;
    }

    const pool = alive.length > 0 ? alive : this.players;
    let best = null;
    let bestHp = -Infinity;
    let tiedBest = false;

    for (const player of pool) {
      const hp = Number(player.hp) || 0;
      if (hp > bestHp) {
        bestHp = hp;
        best = player;
        tiedBest = false;
      } else if (hp === bestHp) {
        tiedBest = true;
      }
    }

    if (tiedBest || !best) {
      this.winnerTicketId = "";
      this.phase = "round_end";
      this.timerEndsAtMs = nowMs + this.limits.roundEndTimeoutMs;
      this.dirty = true;
      return;
    }

    this.awardRoundWin(best.ticketId, nowMs);
  }

  nextRound(nowMs) {
    const winner = this.players.find((p) => (this.roundWins[p.ticketId] || 0) >= this.roundsToWin);
    if (winner) {
      this.winnerTicketId = winner.ticketId;
      this.phase = "match_end";
      this.finished = true;
      this.dirty = true;
      return;
    }

    this.round += 1;
    this.beginWeaponPick(nowMs);
  }

  tick(nowMs) {
    this.serverTick += 1;
    const dtSec = 1 / this.limits.tickRateHz;

    if (nowMs >= this.timerEndsAtMs) {
      switch (this.phase) {
        case "waiting":
          if (this.canStartMatch()) {
            this.beginPrep(nowMs);
          } else {
            const connected = this.players.filter((p) => p.connected);
            if (connected.length >= 1) {
              this.winnerTicketId = connected[0].ticketId;
            }
            this.phase = "match_end";
            this.finished = true;
          }
          this.dirty = true;
          break;
        case "prep":
          this.beginWeaponPick(nowMs);
          break;
        case "weapon_pick":
          this.aiController.tickWeaponPick(nowMs);
          for (const p of this.players) {
            if (!p.hasWeapon || p.weaponKind === null) {
              const randomKind = Math.floor(Math.random() * 4);
              combat.equipWeapon(p, randomKind);
            }
          }
          this.startFight(nowMs);
          break;
        case "fight":
          this.resolveRoundTimeout(nowMs);
          break;
        case "round_end":
          this.nextRound(nowMs);
          break;
        default:
          break;
      }
    }

    if (this.phase === "fight") {
      this.aiController.tick(nowMs);
      for (const p of this.players) {
        if (!p.isAiBot) {
          movement.tickMovement(p, dtSec);
        }
      }
    } else if (this.phase === "prep") {
      this.aiController.tick(nowMs);
    }
  }

  shouldSendSnapshot(nowMs, snapshotIntervalMs) {
    if (!this.dirty && nowMs - this.lastSnapshotMs < snapshotIntervalMs) {
      return false;
    }
    if (nowMs - this.lastSnapshotMs >= snapshotIntervalMs) {
      return true;
    }
    return this.dirty;
  }

  buildSnapshotForViewer(viewerTicketId) {
    return buildSnapshotForViewer(this, viewerTicketId, this.limits.tickRateHz);
  }

  buildStateForTicket(ticketId) {
    return buildMatchStateForTicket(this, ticketId);
  }
}

module.exports = Duel;
