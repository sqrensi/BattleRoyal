"use strict";

const VERSION = 13;
const MAX_SKIN_ID = 48;
const MAX_TICKET_LEN = 64;
const MAX_MODEL_LEN = 64;

function writeU8(chunks, value) {
  const b = Buffer.allocUnsafe(1);
  b.writeUInt8(value & 0xff, 0);
  chunks.push(b);
}

function writeU16(chunks, value) {
  const b = Buffer.allocUnsafe(2);
  b.writeUInt16LE(value & 0xffff, 0);
  chunks.push(b);
}

function writeU32(chunks, value) {
  const b = Buffer.allocUnsafe(4);
  b.writeUInt32LE(value >>> 0, 0);
  chunks.push(b);
}

function writeF32(chunks, value) {
  const b = Buffer.allocUnsafe(4);
  b.writeFloatLE(Number(value) || 0, 0);
  chunks.push(b);
}

function writeUtf8(chunks, str, maxLen) {
  const raw = Buffer.from(String(str || ""), "utf8");
  const len = Math.min(raw.length, maxLen);
  writeU8(chunks, len);
  if (len > 0) {
    chunks.push(len === raw.length ? raw : raw.subarray(0, len));
  }
}

function writeSlotId(chunks, id) {
  writeUtf8(chunks, id, MAX_SKIN_ID);
}

function buildFlags2(player) {
  let flags = 0;
  if (player.isDead) {
    flags |= 1;
  }
  if (player.isGrounded !== false) {
    flags |= 2;
  }
  if (player.isCrouching) {
    flags |= 4;
  }
  if (player.isSprinting) {
    flags |= 8;
  }
  if (player.isAiming) {
    flags |= 16;
  }
  if (player.isHolstered) {
    flags |= 32;
  }
  if (player.hasWeapon) {
    flags |= 64;
  }
  if (player.isUsingMedkit) {
    flags |= 128;
  }
  if (player.isSwimming) {
    flags |= 256;
  }
  return flags;
}

function writeRecentShots(chunks, shots) {
  const items = Array.isArray(shots) ? shots : [];
  const count = Math.min(items.length, 8);
  writeU8(chunks, count);
  for (let i = 0; i < count; i++) {
    const s = items[i] || {};
    writeU32(chunks, s.seq || 0);
    writeF32(chunks, s.originX);
    writeF32(chunks, s.originY);
    writeF32(chunks, s.originZ);
    writeF32(chunks, s.dirX);
    writeF32(chunks, s.dirY);
    writeF32(chunks, s.dirZ);
    writeF32(chunks, s.endX);
    writeF32(chunks, s.endY);
    writeF32(chunks, s.endZ);
    writeU8(chunks, s.hasEndPoint ? 1 : 0);
  }
}

function writeHistory(chunks, history) {
  const items = Array.isArray(history) ? history : [];
  const count = Math.min(items.length, 16);
  writeU8(chunks, count);
  for (let i = 0; i < count; i++) {
    const s = items[i] || {};
    writeU32(chunks, s.sampleTick || 0);
    writeF32(chunks, s.x);
    writeF32(chunks, s.y);
    writeF32(chunks, s.z);
    writeF32(chunks, s.yaw);
    writeF32(chunks, s.velX);
    writeF32(chunks, s.velY);
    writeF32(chunks, s.velZ);
  }
}

function encodePlayer(chunks, player) {
  writeUtf8(chunks, player.ticketId, MAX_TICKET_LEN);
  writeUtf8(chunks, player.characterModel || "", MAX_MODEL_LEN);
  writeSlotId(chunks, player.skinShirt);
  writeSlotId(chunks, player.skinPants);
  writeSlotId(chunks, player.skinBoots);
  writeSlotId(chunks, player.skinGloves);
  writeSlotId(chunks, player.skinFace);
  writeSlotId(chunks, player.skinHair);
  writeSlotId(chunks, player.skinWeaponAssault);
  writeSlotId(chunks, player.skinWeaponSniper);
  writeSlotId(chunks, player.skinWeaponPistol);
  writeSlotId(chunks, player.skinWeaponMp7);

  writeU32(chunks, player.sampleTick || 0);
  const pos = player.position || {};
  writeF32(chunks, pos.x);
  writeF32(chunks, pos.y);
  writeF32(chunks, pos.z);
  writeF32(chunks, player.yaw);
  writeF32(chunks, player.velX);
  writeF32(chunks, player.velY);
  writeF32(chunks, player.velZ);
  writeU16(chunks, buildFlags2(player));
  writeU8(chunks, player.jumpState || 0);
  writeF32(chunks, player.lookPitch);
  writeU32(chunks, player.shotSeq || 0);
  writeU32(chunks, player.reloadSeq || 0);
  writeU32(chunks, player.hitPlayerSeq || 0);
  writeU32(chunks, player.footstepSeq || 0);
  writeF32(chunks, player.wallAvoidBlend);
  writeF32(chunks, player.animSpeed);
  writeF32(chunks, player.animPhase);
  writeU32(chunks, player.deathSeq || 0);
  writeF32(chunks, player.deathFallDirX);
  writeF32(chunks, player.deathFallDirY);
  writeF32(chunks, player.deathFallDirZ);
  writeF32(chunks, player.shotOriginX);
  writeF32(chunks, player.shotOriginY);
  writeF32(chunks, player.shotOriginZ);
  writeF32(chunks, player.shotDirX);
  writeF32(chunks, player.shotDirY);
  writeF32(chunks, player.shotDirZ);
  writeF32(chunks, player.moveInputX);
  writeF32(chunks, player.moveInputZ);
  writeF32(chunks, player.shotEndX);
  writeF32(chunks, player.shotEndY);
  writeF32(chunks, player.shotEndZ);
  writeU8(chunks, player.shotHasEndPoint ? 1 : 0);
  // Field order must match RealtimeSnapshotBinaryCodec.cs (v7–v10 before v5 recentShots, then history).
  writeU32(chunks, player.weaponPickupSeq || 0);
  writeF32(chunks, player.medkitRemainingSeconds || 0);
  writeU8(chunks, player.medkitCount || 0);
  writeU8(chunks, player.hasWeapon ? (Number(player.weaponKind) & 0xff) : 0);
  writeU8(chunks, Number(player.weaponSlot0Kind ?? 255) & 0xff);
  writeU8(chunks, Number(player.weaponSlot1Kind ?? 255) & 0xff);
  writeU8(chunks, Number(player.activeWeaponSlot ?? 255) & 0xff);
  writeRecentShots(chunks, player.recentShots);
  writeHistory(chunks, player.history);
}

function encodeSnapshotRts1(frame) {
  const chunks = [Buffer.from("RTS1")];
  writeU8(chunks, VERSION);
  writeU32(chunks, frame.serverTick || 0);
  writeU16(chunks, frame.serverTickRate || 20);
  writeU16(chunks, frame.movementSampleRateHz || frame.serverTickRate || 20);

  const selfAuth = frame.selfAuthoritative || null;
  writeU8(chunks, selfAuth ? 1 : 0);
  if (selfAuth) {
    const pos = selfAuth.position || {};
    writeU32(chunks, selfAuth.sampleTick || 0);
    writeF32(chunks, pos.x);
    writeF32(chunks, pos.y);
    writeF32(chunks, pos.z);
    writeF32(chunks, selfAuth.yaw || 0);
  }

  const players = Array.isArray(frame.players) ? frame.players : [];
  writeU8(chunks, Math.min(players.length, 255));
  for (const player of players) {
    encodePlayer(chunks, player);
  }

  return Buffer.concat(chunks);
}

module.exports = {
  VERSION,
  encodeSnapshotRts1,
};
