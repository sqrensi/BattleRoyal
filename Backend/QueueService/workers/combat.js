"use strict";

const { WEAPON_SLOT_EMPTY } = require("./player");

const WEAPONS = {
  0: { id: "assault_rifle", damage: 25, range: 120, fireIntervalMs: 100, magSize: 30, reloadMs: 2000 },
  1: { id: "sniper_rifle", damage: 80, range: 200, fireIntervalMs: 900, magSize: 7, reloadMs: 2500 },
  2: { id: "pistol", damage: 20, range: 60, fireIntervalMs: 200, magSize: 12, reloadMs: 1500 },
  3: { id: "mp7", damage: 18, range: 80, fireIntervalMs: 75, magSize: 30, reloadMs: 1800 },
};

function getWeapon(kind) {
  return WEAPONS[Number(kind)] || WEAPONS[0];
}

function distance3(ax, ay, az, bx, by, bz) {
  const dx = ax - bx;
  const dy = ay - by;
  const dz = az - bz;
  return Math.sqrt(dx * dx + dy * dy + dz * dz);
}

function canFire(attacker, nowMs) {
  if (!attacker.alive || attacker.weaponKind === null) {
    return { ok: false, reason: "no_weapon" };
  }
  if (nowMs < attacker.reloadingUntilMs) {
    return { ok: false, reason: "reloading" };
  }
  if (attacker.ammoInMag <= 0) {
    return { ok: false, reason: "empty_mag" };
  }

  const weapon = getWeapon(attacker.weaponKind);
  if (nowMs - attacker.lastShotMs < weapon.fireIntervalMs) {
    return { ok: false, reason: "fire_rate" };
  }

  if (nowMs - attacker.shotWindowStartMs > 1000) {
    attacker.shotWindowStartMs = nowMs;
    attacker.shotsThisSecond = 0;
  }
  attacker.shotsThisSecond += 1;
  if (attacker.shotsThisSecond > 25) {
    attacker.cheatFlags |= 1;
    return { ok: false, reason: "fire_spam" };
  }

  return { ok: true, weapon };
}

function validateHit(attacker, target, message, limits) {
  if (!target.alive) {
    return { ok: false, reason: "target_dead" };
  }

  const weapon = getWeapon(attacker.weaponKind);
  const hitX = Number(message.hitX ?? target.x);
  const hitY = Number(message.hitY ?? target.y);
  const hitZ = Number(message.hitZ ?? target.z);

  const dist = distance3(attacker.x, attacker.y, attacker.z, hitX, hitY, hitZ);
  if (dist > weapon.range + 2) {
    attacker.cheatFlags |= 2;
    return { ok: false, reason: "range" };
  }

  const toTarget = distance3(attacker.x, attacker.y, attacker.z, target.x, target.y, target.z);
  if (toTarget > weapon.range + limits.playerHitRadius) {
    return { ok: false, reason: "out_of_range" };
  }

  const centerY = target.y + 0.9;
  const miss = distance3(hitX, hitY, hitZ, target.x, centerY, target.z);
  if (miss > limits.playerHitRadius * 1.35) {
    return { ok: false, reason: "miss" };
  }

  let damage = weapon.damage;
  const zone = String(message.hitZone || "body").toLowerCase();
  if (zone === "head") {
    damage = Math.round(damage * 2);
  } else if (zone === "leg") {
    damage = Math.round(damage * 0.7);
  }

  return { ok: true, damage, weapon };
}

function applyHit(attacker, target, damage, nowMs) {
  target.hp = Math.max(0, target.hp - damage);
  attacker.ammoInMag = Math.max(0, attacker.ammoInMag - 1);
  attacker.lastShotMs = nowMs || Date.now();

  if (target.hp <= 0) {
    target.alive = false;
    return true;
  }
  return false;
}

const DAMAGE_BY_HIT_ZONE = {
  leg: 15,
  body: 25,
  neck: 80,
  head: 100,
};

function resolveHitDamage(message, weapon) {
  const hitZone = typeof message.hitZone === "string" ? message.hitZone.trim().toLowerCase() : "";
  if (Object.prototype.hasOwnProperty.call(DAMAGE_BY_HIT_ZONE, hitZone)) {
    return DAMAGE_BY_HIT_ZONE[hitZone];
  }

  const clientDamage = Number(message.damage);
  if (Number.isFinite(clientDamage) && clientDamage > 0) {
    return Math.max(0, Math.min(1000, clientDamage));
  }

  let damage = weapon.damage;
  if (hitZone === "head") {
    damage = Math.round(damage * 2);
  } else if (hitZone === "leg") {
    damage = Math.round(damage * 0.7);
  }
  return damage;
}

function clearWeapon(player) {
  if (!player) {
    return;
  }

  player.weaponKind = null;
  player.weaponSlot0Kind = WEAPON_SLOT_EMPTY;
  player.weaponSlot1Kind = WEAPON_SLOT_EMPTY;
  player.activeWeaponSlot = WEAPON_SLOT_EMPTY;
  player.isHolstered = true;
  player.hasWeapon = false;
  player.ammoInMag = 0;
  player.spareAmmo = 0;
  player.reloadingUntilMs = 0;
}

function equipWeapon(player, weaponKind) {
  const kind = Math.max(0, Math.min(3, Math.floor(Number(weaponKind))));
  const weapon = getWeapon(kind);
  player.weaponKind = kind;
  player.weaponSlot0Kind = kind;
  player.weaponSlot1Kind = WEAPON_SLOT_EMPTY;
  player.activeWeaponSlot = 0;
  player.isHolstered = false;
  player.hasWeapon = true;
  player.ammoInMag = weapon.magSize;
  player.spareAmmo = weapon.magSize * 2;
  player.reloadingUntilMs = 0;
}

function tryReload(player, nowMs) {
  if (player.weaponKind === null || nowMs < player.reloadingUntilMs) {
    return false;
  }
  const weapon = getWeapon(player.weaponKind);
  if (player.ammoInMag >= weapon.magSize || player.spareAmmo <= 0) {
    return false;
  }
  player.reloadingUntilMs = nowMs + weapon.reloadMs;
  const need = weapon.magSize - player.ammoInMag;
  const take = Math.min(need, player.spareAmmo);
  player.ammoInMag += take;
  player.spareAmmo -= take;
  return true;
}

module.exports = {
  WEAPONS,
  getWeapon,
  canFire,
  validateHit,
  applyHit,
  equipWeapon,
  clearWeapon,
  tryReload,
  resolveHitDamage,
};
