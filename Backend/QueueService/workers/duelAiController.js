"use strict";

const combat = require("./combat");
const { applyProfileToPlayer } = require("./aiBotProfile");
const { getArenaBounds, getTeamCenter } = require("./spawnTable");

const ARENA_BOUNDS = getArenaBounds();

class DuelAiController {
  constructor(duel, limits) {
    this.duel = duel;
    this.limits = limits;
    this.states = new Map();
    this.lastThinkMs = 0;
    this.thinkIntervalMs = Math.max(50, Math.floor(1000 / Math.max(10, limits.aiThinkHz || 10)));
  }

  initializeBot(player, profile, spawn) {
    player.isAiBot = true;
    applyProfileToPlayer(player, profile, spawn);
    this.states.set(player.ticketId, {
      profile: profile || {},
      wanderAngle: Math.random() * Math.PI * 2,
      nextShotMs: 0,
      nextReloadMs: 0,
      strafeDir: Math.random() < 0.5 ? -1 : 1,
      weaponPicked: false,
    });
  }

  getState(ticketId) {
    return this.states.get(ticketId) || null;
  }

  tickWeaponPick(nowMs) {
    for (const player of this.duel.players) {
      if (!player.isAiBot || !player.alive) {
        continue;
      }

      const state = this.getState(player.ticketId);
      if (!state || state.weaponPicked) {
        continue;
      }

      if (nowMs < (state.nextShotMs || 0)) {
        continue;
      }

      const kind = Math.floor(Math.random() * 4);
      this.duel.onWeaponPick(player.ticketId, { weaponKind: kind }, nowMs);
      state.weaponPicked = true;
      state.nextShotMs = nowMs + 400 + Math.floor(Math.random() * 1200);
      this.duel.dirty = true;
    }
  }

  onPhaseChange(phase) {
    for (const state of this.states.values()) {
      state.weaponPicked = false;
      state.nextShotMs = Date.now() + 300 + Math.floor(Math.random() * 800);
    }
  }

  onSpawnPositionsApplied() {
    for (const player of this.duel.players) {
      if (!player.isAiBot) {
        continue;
      }

      const state = this.getState(player.ticketId);
      if (state) {
        state.wanderAngle = Math.random() * Math.PI * 2;
      }

      player.velX = 0;
      player.velZ = 0;
      player.animSpeed = 0;
      player.hasPose = true;
      player.lastPoseMs = Date.now();
    }
    this.duel.dirty = true;
  }

  tick(nowMs) {
    if (nowMs - this.lastThinkMs < this.thinkIntervalMs) {
      return;
    }
    this.lastThinkMs = nowMs;

    const phase = this.duel.phase;
    if (phase !== "fight" && phase !== "prep" && phase !== "weapon_pick") {
      return;
    }

    for (const player of this.duel.players) {
      if (!player.isAiBot || !player.alive) {
        continue;
      }

      const opponent = this.duel.getOpponent(player.ticketId);
      if (!opponent) {
        continue;
      }

      const state = this.getState(player.ticketId);
      if (!state) {
        continue;
      }

      if (phase === "prep" || phase === "weapon_pick") {
        this.tickPrepMovement(player, state, nowMs);
        continue;
      }

      this.tickCombat(player, opponent, state, nowMs);
    }
  }

  getTeamIndex(ticketId) {
    if (typeof this.duel.getTeamIndex === "function") {
      return Math.max(0, this.duel.getTeamIndex(ticketId));
    }
    return 0;
  }

  tickPrepMovement(player, state, nowMs) {
    const center = getTeamCenter(this.getTeamIndex(player.ticketId));
    state.wanderAngle += 0.03 + Math.random() * 0.02;
    const radius = 2.2;
    const targetX = center.x + Math.cos(state.wanderAngle) * radius;
    const targetZ = center.z + Math.sin(state.wanderAngle) * radius;
    this.moveToward(player, targetX, targetZ, 2.4, this.thinkIntervalMs / 1000);
    player.hasPose = true;
    player.lastPoseMs = nowMs;
    this.duel.dirty = true;
  }

  tickCombat(player, opponent, state, nowMs) {
    if (!opponent.alive) {
      return;
    }

    const skill = Math.max(0.2, Math.min(1, Number(state.profile.skill) || 0.65));
    const aggression = Math.max(0.2, Math.min(1, Number(state.profile.aggression) || 0.6));
    const dx = opponent.x - player.x;
    const dz = opponent.z - player.z;
    const dist = Math.sqrt(dx * dx + dz * dz) || 0.001;

    const preferredRange = player.weaponKind === 1 ? 14 : player.weaponKind === 2 ? 8 : 11;
    let moveSpeed = 4.5 + aggression * 2.5;

    if (dist > preferredRange + 2) {
      const targetX = opponent.x - (dx / dist) * preferredRange;
      const targetZ = opponent.z - (dz / dist) * preferredRange;
      this.moveToward(player, targetX, targetZ, moveSpeed, this.thinkIntervalMs / 1000);
    } else if (dist < preferredRange - 2) {
      const targetX = player.x - (dx / dist) * 2.5;
      const targetZ = player.z - (dz / dist) * 2.5;
      this.moveToward(player, targetX, targetZ, moveSpeed * 0.85, this.thinkIntervalMs / 1000);
    } else {
      state.strafeDir = Math.random() < 0.04 ? -state.strafeDir : state.strafeDir;
      const perpX = -dz / dist;
      const perpZ = dx / dist;
      const targetX = player.x + perpX * state.strafeDir * 2.2;
      const targetZ = player.z + perpZ * state.strafeDir * 2.2;
      this.moveToward(player, targetX, targetZ, moveSpeed * 0.7, this.thinkIntervalMs / 1000);
    }

    this.aimAt(player, opponent, skill);
    player.isAiming = dist < 22;
    player.hasPose = true;
    player.lastPoseMs = nowMs;

    if (player.ammoInMag <= 0 && nowMs >= (state.nextReloadMs || 0)) {
      if (combat.tryReload(player, nowMs)) {
        state.nextReloadMs = nowMs + 500;
      }
    }

    if (dist <= 24 && nowMs >= (state.nextShotMs || 0)) {
      this.tryShoot(player, opponent, state, skill, nowMs);
    }

    this.duel.dirty = true;
  }

  moveToward(player, targetX, targetZ, speed, dtSec) {
    const clampedX = Math.max(ARENA_BOUNDS.minX, Math.min(ARENA_BOUNDS.maxX, targetX));
    const clampedZ = Math.max(ARENA_BOUNDS.minZ, Math.min(ARENA_BOUNDS.maxZ, targetZ));
    const dx = clampedX - player.x;
    const dz = clampedZ - player.z;
    const dist = Math.sqrt(dx * dx + dz * dz);
    if (dist < 0.05) {
      player.velX = 0;
      player.velZ = 0;
      player.animSpeed = 0;
      return;
    }

    const step = Math.min(dist, speed * dtSec);
    player.x += (dx / dist) * step;
    player.z += (dz / dist) * step;
    player.yaw = (Math.atan2(dx, dz) * 180) / Math.PI;
    player.velX = (dx / dist) * speed;
    player.velZ = (dz / dist) * speed;
    player.moveInputX = dx / dist;
    player.moveInputZ = dz / dist;
    player.animSpeed = speed;
    player.isSprinting = speed > 5;
    player.animPhase = (player.animPhase + step * 0.35) % 1;
  }

  aimAt(player, target, skill) {
    const dx = target.x - player.x;
    const dz = target.z - player.z;
    const dy = (target.y + 0.9) - (player.y + 1.35);
    const horiz = Math.sqrt(dx * dx + dz * dz) || 0.001;
    player.yaw = (Math.atan2(dx, dz) * 180) / Math.PI;
    player.lookPitch = Math.max(-25, Math.min(25, (Math.atan2(dy, horiz) * 180) / Math.PI));
    player.lookPitch += (1 - skill) * (Math.random() - 0.5) * 8;
  }

  tryShoot(player, target, state, skill, nowMs) {
    const weapon = combat.getWeapon(player.weaponKind);
    const baseInterval = weapon.fireIntervalMs + (1 - skill) * 180;
    state.nextShotMs = nowMs + baseInterval + Math.floor(Math.random() * 120);

    const hitChance = 0.25 + skill * 0.55;
    if (Math.random() > hitChance) {
      this.emitShotFx(player, target, skill, false, nowMs);
      return;
    }

    this.emitShotFx(player, target, skill, true, nowMs);

    const centerY = target.y + 0.9;
    const missRadius = this.limits.playerHitRadius * (1.1 - skill * 0.75);
    const hitX = target.x + (Math.random() - 0.5) * missRadius;
    const hitY = centerY + (Math.random() - 0.5) * missRadius * 0.8;
    const hitZ = target.z + (Math.random() - 0.5) * missRadius;
    const hitZone = Math.random() < 0.08 + skill * 0.12 ? "head" : "body";

    this.duel.onHit(player.ticketId, {
      targetTicketId: target.ticketId,
      hitX,
      hitY,
      hitZ,
      hitZone,
      dirX: hitX - player.x,
      dirY: hitY - (player.y + 1.35),
      dirZ: hitZ - player.z,
    }, nowMs);
  }

  emitShotFx(player, target, skill, onTarget, nowMs) {
    const eyeY = player.y + 1.35;
    const endX = onTarget
      ? target.x + (Math.random() - 0.5) * 0.2
      : target.x + (Math.random() - 0.5) * 4;
    const endY = onTarget
      ? target.y + 0.9 + (Math.random() - 0.5) * 0.3
      : target.y + 0.5 + (Math.random() - 0.5) * 2;
    const endZ = onTarget
      ? target.z + (Math.random() - 0.5) * 0.2
      : target.z + (Math.random() - 0.5) * 4;

    let dirX = endX - player.x;
    let dirY = endY - eyeY;
    let dirZ = endZ - player.z;
    const mag = Math.sqrt(dirX * dirX + dirY * dirY + dirZ * dirZ) || 1;
    dirX /= mag;
    dirY /= mag;
    dirZ /= mag;

    player.shotSeq = Math.max(0, Number(player.shotSeq) || 0) + 1;
    this.duel.onShot(player.ticketId, {
      shotSeq: player.shotSeq,
      shotOriginX: player.x,
      shotOriginY: eyeY,
      shotOriginZ: player.z,
      shotDirX: dirX,
      shotDirY: dirY,
      shotDirZ: dirZ,
      shotEndX: endX,
      shotEndY: endY,
      shotEndZ: endZ,
      shotHasEndPoint: true,
    }, nowMs);
  }
}

module.exports = DuelAiController;
