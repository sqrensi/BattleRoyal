"use strict";

const WEAPON_SLOT_EMPTY = 255;
const STATE_HISTORY_BROADCAST_MAX = 16;
const STATE_HISTORY_STORE_MAX = STATE_HISTORY_BROADCAST_MAX * 3;

class Player {
  constructor(data) {
    this.ticketId = data.ticketId;
    this.playerId = data.playerId || data.ticketId;
    this.nickname = data.nickname || "Player";
    this.duelRating = Number.isFinite(data.duelRating) ? Math.max(0, data.duelRating) : 1000;

    this.hp = 100;
    this.maxHp = 100;
    this.alive = true;
    this.roundWins = 0;
    this.matchKills = 0;
    this.respawnAtMs = 0;

    this.weaponKind = null;
    this.weaponSlot0Kind = WEAPON_SLOT_EMPTY;
    this.weaponSlot1Kind = WEAPON_SLOT_EMPTY;
    this.activeWeaponSlot = WEAPON_SLOT_EMPTY;
    this.isHolstered = true;
    this.hasWeapon = false;
    this.ammoInMag = 0;
    this.spareAmmo = 0;
    this.reloadingUntilMs = 0;
    this.lastShotMs = 0;
    this.shotsThisSecond = 0;
    this.shotWindowStartMs = 0;

    this.x = data.spawnX || 0;
    this.y = data.spawnY || 0;
    this.z = data.spawnZ || 0;
    this.yaw = 0;
    this.velX = 0;
    this.velY = 0;
    this.velZ = 0;
    this.isGrounded = true;
    this.jumpState = 0;
    this.moveInputX = 0;
    this.moveInputZ = 0;
    this.animSpeed = 0;
    this.animPhase = 0;
    this.wallAvoidBlend = 0;
    this.isCrouching = false;
    this.isSprinting = false;
    this.isSwimming = false;
    this.isAiming = false;

    this.shotSeq = 0;
    this.reloadSeq = 0;
    this.hitPlayerSeq = 0;
    this.footstepSeq = 0;
    this.deathSeq = 0;
    this.shotOriginX = 0;
    this.shotOriginY = 0;
    this.shotOriginZ = 0;
    this.shotDirX = 0;
    this.shotDirY = 0;
    this.shotDirZ = 0;
    this.shotEndX = 0;
    this.shotEndY = 0;
    this.shotEndZ = 0;
    this.shotHasEndPoint = false;
    this.shotRing = [];

    this.connected = false;
    this.hasPose = false;
    this.poseSampleSeq = 0;
    this.stateHistory = [];
    this.lastPoseMs = 0;
    this.lastSeq = null;
    this.respawnPoseGraceUntilMs = 0;
    this.cheatFlags = 0;
    this.isAiBot = !!data.isAiBot;

    this.characterModel = "";
    this.skinShirt = "";
    this.skinPants = "";
    this.skinBoots = "";
    this.skinGloves = "";
    this.skinFace = "";
    this.skinHair = "";
    this.skinWeaponAssault = "";
    this.skinWeaponSniper = "";
    this.skinWeaponPistol = "";
    this.skinWeaponMp7 = "";
    this.lookPitch = 0;
  }

  applyPresence(message) {
    if (!message) {
      return;
    }
    if (typeof message.characterModel === "string") {
      this.characterModel = message.characterModel;
    }
    if (typeof message.skinShirt === "string") {
      this.skinShirt = message.skinShirt;
    }
    if (typeof message.skinPants === "string") {
      this.skinPants = message.skinPants;
    }
    if (typeof message.skinBoots === "string") {
      this.skinBoots = message.skinBoots;
    }
    if (typeof message.skinGloves === "string") {
      this.skinGloves = message.skinGloves;
    }
    if (typeof message.skinFace === "string") {
      this.skinFace = message.skinFace;
    }
    if (typeof message.skinHair === "string") {
      this.skinHair = message.skinHair;
    }
    if (typeof message.skinWeaponAssault === "string") {
      this.skinWeaponAssault = message.skinWeaponAssault;
    }
    if (typeof message.skinWeaponSniper === "string") {
      this.skinWeaponSniper = message.skinWeaponSniper;
    }
    if (typeof message.skinWeaponPistol === "string") {
      this.skinWeaponPistol = message.skinWeaponPistol;
    }
    if (typeof message.skinWeaponMp7 === "string") {
      this.skinWeaponMp7 = message.skinWeaponMp7;
    }
    if (Number.isFinite(Number(message.lookPitch))) {
      this.lookPitch = Number(message.lookPitch);
    }
  }

  applyNetworkPose(message) {
    if (!message) {
      return;
    }

    this.applyPresence(message);

    if (Number.isFinite(Number(message.isCrouching)) || typeof message.isCrouching === "boolean") {
      this.isCrouching = !!message.isCrouching;
    }
    if (Number.isFinite(Number(message.isSprinting)) || typeof message.isSprinting === "boolean") {
      this.isSprinting = !!message.isSprinting;
    }
    if (Number.isFinite(Number(message.isSwimming)) || typeof message.isSwimming === "boolean") {
      this.isSwimming = !!message.isSwimming;
    }
    if (Number.isFinite(Number(message.isAiming)) || typeof message.isAiming === "boolean") {
      this.isAiming = !!message.isAiming;
    }
    if (Number.isFinite(Number(message.isHolstered)) || typeof message.isHolstered === "boolean") {
      this.isHolstered = !!message.isHolstered;
    }
    if (Number.isFinite(Number(message.isGrounded)) || typeof message.isGrounded === "boolean") {
      this.isGrounded = message.isGrounded !== false;
    }
    if (Number.isFinite(Number(message.jumpState))) {
      this.jumpState = Number(message.jumpState);
    }
    if (Number.isFinite(Number(message.animSpeed))) {
      this.animSpeed = Number(message.animSpeed);
    }
    if (Number.isFinite(Number(message.animPhase))) {
      this.animPhase = Number(message.animPhase);
    }
    if (Number.isFinite(Number(message.wallAvoidBlend))) {
      this.wallAvoidBlend = Number(message.wallAvoidBlend);
    }
    if (Number.isFinite(Number(message.moveInputX))) {
      this.moveInputX = Number(message.moveInputX);
    }
    if (Number.isFinite(Number(message.moveInputZ))) {
      this.moveInputZ = Number(message.moveInputZ);
    }
    if (Number.isFinite(Number(message.reloadSeq))) {
      this.reloadSeq = Number(message.reloadSeq);
    }
    if (Number.isFinite(Number(message.hitPlayerSeq))) {
      this.hitPlayerSeq = Number(message.hitPlayerSeq);
    }
    if (Number.isFinite(Number(message.footstepSeq))) {
      this.footstepSeq = Number(message.footstepSeq);
    }

    const weaponKind = Number(message.weaponKind);
    if (Number.isFinite(weaponKind) && weaponKind >= 0 && weaponKind <= 3) {
      this.weaponKind = weaponKind;
      this.hasWeapon = true;
    }
    const slot0 = Number(message.weaponSlot0Kind);
    if (Number.isFinite(slot0)) {
      this.weaponSlot0Kind = slot0;
      if (slot0 !== WEAPON_SLOT_EMPTY && slot0 >= 0 && slot0 <= 3) {
        this.hasWeapon = true;
      }
    }
    const slot1 = Number(message.weaponSlot1Kind);
    if (Number.isFinite(slot1)) {
      this.weaponSlot1Kind = slot1;
    }
    const activeSlot = Number(message.activeWeaponSlot);
    if (Number.isFinite(activeSlot)) {
      this.activeWeaponSlot = activeSlot;
    }

    const shotSeq = Number(message.shotSeq);
    if (Number.isFinite(shotSeq) && shotSeq > this.shotSeq) {
      this.shotSeq = shotSeq;
      if (Number.isFinite(Number(message.shotOriginX))) {
        this.shotOriginX = Number(message.shotOriginX);
        this.shotOriginY = Number(message.shotOriginY ?? 0);
        this.shotOriginZ = Number(message.shotOriginZ ?? 0);
        this.shotDirX = Number(message.shotDirX ?? 0);
        this.shotDirY = Number(message.shotDirY ?? 0);
        this.shotDirZ = Number(message.shotDirZ ?? 0);
        this.shotEndX = Number(message.shotEndX ?? 0);
        this.shotEndY = Number(message.shotEndY ?? 0);
        this.shotEndZ = Number(message.shotEndZ ?? 0);
        this.shotHasEndPoint = !!message.shotHasEndPoint;
      }
    }
  }

  recordShotEvent(message) {
    if (!message) {
      return false;
    }

    const seq = Math.max(0, Math.floor(Number(message.shotSeq ?? message.seq ?? 0)));
    if (seq <= 0) {
      return false;
    }

    const prevSeq = Number.isFinite(this.shotSeq) ? this.shotSeq : 0;
    if (seq < prevSeq) {
      return false;
    }

    const record = {
      seq,
      shotOriginX: Number(message.shotOriginX ?? 0),
      shotOriginY: Number(message.shotOriginY ?? 0),
      shotOriginZ: Number(message.shotOriginZ ?? 0),
      shotDirX: Number(message.shotDirX ?? 0),
      shotDirY: Number(message.shotDirY ?? 0),
      shotDirZ: Number(message.shotDirZ ?? 0),
      shotEndX: Number(message.shotEndX ?? 0),
      shotEndY: Number(message.shotEndY ?? 0),
      shotEndZ: Number(message.shotEndZ ?? 0),
      shotHasEndPoint: !!message.shotHasEndPoint,
    };

    let ring = Array.isArray(this.shotRing) ? this.shotRing.slice() : [];
    const existingIndex = ring.findIndex((item) => item && item.seq === seq);
    if (existingIndex >= 0) {
      ring[existingIndex] = record;
    } else {
      ring.push(record);
      ring.sort((a, b) => (a.seq || 0) - (b.seq || 0));
      while (ring.length > 8) {
        ring.shift();
      }
    }
    this.shotRing = ring;

    this.shotSeq = seq;
    this.shotOriginX = record.shotOriginX;
    this.shotOriginY = record.shotOriginY;
    this.shotOriginZ = record.shotOriginZ;
    this.shotDirX = record.shotDirX;
    this.shotDirY = record.shotDirY;
    this.shotDirZ = record.shotDirZ;
    this.shotEndX = record.shotEndX;
    this.shotEndY = record.shotEndY;
    this.shotEndZ = record.shotEndZ;
    this.shotHasEndPoint = record.shotHasEndPoint;
    return true;
  }

  formatRecentShotsForClient() {
    const ring = Array.isArray(this.shotRing) ? this.shotRing.slice(-8) : [];
    return ring.map((ev) => ({
      seq: ev.seq || 0,
      originX: ev.shotOriginX || 0,
      originY: ev.shotOriginY || 0,
      originZ: ev.shotOriginZ || 0,
      dirX: ev.shotDirX || 0,
      dirY: ev.shotDirY || 0,
      dirZ: ev.shotDirZ || 0,
      endX: ev.shotEndX || 0,
      endY: ev.shotEndY || 0,
      endZ: ev.shotEndZ || 0,
      hasEndPoint: !!ev.shotHasEndPoint,
    }));
  }

  recordStateSample() {
    if (!this.hasPose) {
      return;
    }

    this.poseSampleSeq += 1;
    const entry = {
      sampleTick: this.poseSampleSeq,
      x: this.x,
      y: this.y,
      z: this.z,
      yaw: this.yaw || 0,
      velX: this.velX || 0,
      velY: this.velY || 0,
      velZ: this.velZ || 0,
    };

    const last = this.stateHistory.length > 0
      ? this.stateHistory[this.stateHistory.length - 1]
      : null;
    if (last &&
        last.x === entry.x &&
        last.y === entry.y &&
        last.z === entry.z &&
        Math.abs(last.yaw - entry.yaw) < 0.01 &&
        last.velX === entry.velX &&
        last.velY === entry.velY &&
        last.velZ === entry.velZ) {
      this.poseSampleSeq -= 1;
      return;
    }

    this.stateHistory.push(entry);
    while (this.stateHistory.length > STATE_HISTORY_STORE_MAX) {
      this.stateHistory.shift();
    }
  }

  getBroadcastStateHistory(maxSamples = STATE_HISTORY_BROADCAST_MAX) {
    if (!Array.isArray(this.stateHistory) || this.stateHistory.length === 0) {
      return [];
    }

    const limit = Math.max(1, Math.min(STATE_HISTORY_BROADCAST_MAX, maxSamples));
    return this.stateHistory.slice(-limit).map((entry) => ({
      sampleTick: entry.sampleTick,
      x: entry.x,
      y: entry.y,
      z: entry.z,
      yaw: entry.yaw,
      velX: entry.velX,
      velY: entry.velY,
      velZ: entry.velZ,
    }));
  }

  resetRound(spawn) {
    this.hp = this.maxHp;
    this.alive = true;
    this.weaponKind = null;
    this.weaponSlot0Kind = WEAPON_SLOT_EMPTY;
    this.weaponSlot1Kind = WEAPON_SLOT_EMPTY;
    this.activeWeaponSlot = WEAPON_SLOT_EMPTY;
    this.isHolstered = true;
    this.hasWeapon = false;
    this.ammoInMag = 0;
    this.spareAmmo = 0;
    this.reloadingUntilMs = 0;
    this.lastShotMs = 0;
    this.velX = 0;
    this.velY = 0;
    this.velZ = 0;
    this.poseSampleSeq = 0;
    this.stateHistory = [];
    if (spawn) {
      this.x = spawn.x;
      this.y = spawn.y;
      this.z = spawn.z;
      this.yaw = spawn.yaw || 0;
      this.hasPose = true;
      this.recordStateSample();
    }
  }

  toSnapshot() {
    return {
      ticketId: this.ticketId,
      playerId: this.playerId,
      nickname: this.nickname,
      x: this.x,
      y: this.y,
      z: this.z,
      yaw: this.yaw,
      velX: this.velX,
      velY: this.velY,
      velZ: this.velZ,
      hp: this.hp,
      alive: this.alive,
      weaponKind: this.weaponKind,
      ammoInMag: this.ammoInMag,
      roundWins: this.roundWins,
      isGrounded: this.isGrounded,
    };
  }
}

module.exports = Player;
module.exports.WEAPON_SLOT_EMPTY = WEAPON_SLOT_EMPTY;
