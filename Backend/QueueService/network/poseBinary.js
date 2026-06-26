"use strict";

const SKIN_SLOT_COUNT = 6;
const WEAPON_SKIN_SLOT_COUNT = 4;
const MAX_SKIN_ID_LEN = 48;

function readF32(buffer, offset) {
  return buffer.readFloatLE(offset);
}

function readI16(buffer, offset) {
  return buffer.readInt16LE(offset);
}

function readU16(buffer, offset) {
  return buffer.readUInt16LE(offset);
}

function readI32(buffer, offset) {
  return buffer.readInt32LE(offset);
}

function readU32(buffer, offset) {
  return buffer.readUInt32LE(offset);
}

function readSkinBlock(buffer, offset, slotCount = SKIN_SLOT_COUNT) {
  const ids = [];
  for (let i = 0; i < slotCount; i++) {
    const len = buffer[offset];
    offset += 1;
    if (len > MAX_SKIN_ID_LEN || offset + len > buffer.length) {
      return null;
    }
    ids.push(len > 0 ? buffer.toString("utf8", offset, offset + len) : "");
    offset += len;
  }
  return { ids, offset };
}

function decodeBinaryPose(buffer) {
  if (!Buffer.isBuffer(buffer) || buffer.length < 12) {
    return null;
  }

  if (buffer[0] !== 0x52 || buffer[1] !== 0x54 || buffer[2] !== 0x50 || buffer[3] !== 0x31) {
    return null;
  }

  const version = buffer[4];
  if (version !== 1 && version !== 2 && version !== 3) {
    return null;
  }

  let offset = 5;
  const poseSeq = readI32(buffer, offset);
  offset += 4;
  const modelLen = buffer[offset];
  offset += 1;
  if (offset + modelLen > buffer.length) {
    return null;
  }

  const characterModel = modelLen > 0 ? buffer.toString("utf8", offset, offset + modelLen) : "";
  offset += modelLen;

  let skinShirt = "";
  let skinPants = "";
  let skinBoots = "";
  let skinGloves = "";
  let skinFace = "";
  let skinHair = "";
  let skinWeaponAssault = "";
  let skinWeaponSniper = "";
  let skinWeaponPistol = "";
  let skinWeaponMp7 = "";

  if (version >= 2) {
    if (version >= 3) {
      const legacyBlock = readSkinBlock(buffer, offset, SKIN_SLOT_COUNT + WEAPON_SKIN_SLOT_COUNT);
      if (!legacyBlock) {
        return null;
      }
      offset = legacyBlock.offset;
      skinShirt = legacyBlock.ids[0] || "";
      skinPants = legacyBlock.ids[1] || "";
      skinBoots = legacyBlock.ids[2] || "";
      skinGloves = legacyBlock.ids[3] || "";
      skinFace = legacyBlock.ids[4] || "";
      skinHair = legacyBlock.ids[5] || "";
      skinWeaponAssault = legacyBlock.ids[6] || "";
      skinWeaponSniper = legacyBlock.ids[7] || "";
      skinWeaponPistol = legacyBlock.ids[8] || "";
      skinWeaponMp7 = legacyBlock.ids[9] || "";
    } else {
      const clothingBlock = readSkinBlock(buffer, offset, SKIN_SLOT_COUNT);
      if (!clothingBlock) {
        return null;
      }
      offset = clothingBlock.offset;
      skinShirt = clothingBlock.ids[0] || "";
      skinPants = clothingBlock.ids[1] || "";
      skinBoots = clothingBlock.ids[2] || "";
      skinGloves = clothingBlock.ids[3] || "";
      skinFace = clothingBlock.ids[4] || "";
      skinHair = clothingBlock.ids[5] || "";
    }
  }

  const minSize = offset + 121;
  if (buffer.length < minSize) {
    return null;
  }

  const x = readF32(buffer, offset); offset += 4;
  const y = readF32(buffer, offset); offset += 4;
  const z = readF32(buffer, offset); offset += 4;
  const yaw = readF32(buffer, offset); offset += 4;
  const lookPitch = readF32(buffer, offset); offset += 4;
  const flags = readU16(buffer, offset); offset += 2;
  const jumpState = buffer[offset]; offset += 1;
  const weaponKind = buffer[offset]; offset += 1;
  const weaponSlot0Kind = buffer[offset]; offset += 1;
  const weaponSlot1Kind = buffer[offset]; offset += 1;
  const activeWeaponSlot = buffer[offset]; offset += 1;
  const activeWeaponMagAmmo = readI16(buffer, offset); offset += 2;
  const weaponPickupSeq = readU32(buffer, offset); offset += 4;
  const shotSeq = readU32(buffer, offset); offset += 4;
  const reloadSeq = readU32(buffer, offset); offset += 4;
  const hitPlayerSeq = readU32(buffer, offset); offset += 4;
  const footstepSeq = readU32(buffer, offset); offset += 4;
  const deathSeq = readU32(buffer, offset); offset += 4;
  const animSpeed = readF32(buffer, offset); offset += 4;
  const animPhase = readF32(buffer, offset); offset += 4;
  const wallAvoidBlend = readF32(buffer, offset); offset += 4;
  const moveInputX = readF32(buffer, offset); offset += 4;
  const moveInputZ = readF32(buffer, offset); offset += 4;
  const deathFallDirX = readF32(buffer, offset); offset += 4;
  const deathFallDirY = readF32(buffer, offset); offset += 4;
  const deathFallDirZ = readF32(buffer, offset); offset += 4;
  const shotOriginX = readF32(buffer, offset); offset += 4;
  const shotOriginY = readF32(buffer, offset); offset += 4;
  const shotOriginZ = readF32(buffer, offset); offset += 4;
  const shotDirX = readF32(buffer, offset); offset += 4;
  const shotDirY = readF32(buffer, offset); offset += 4;
  const shotDirZ = readF32(buffer, offset); offset += 4;
  const shotEndX = readF32(buffer, offset); offset += 4;
  const shotEndY = readF32(buffer, offset); offset += 4;
  const shotEndZ = readF32(buffer, offset); offset += 4;

  if (version === 2 && offset < buffer.length) {
    const weaponBlock = readSkinBlock(buffer, offset, WEAPON_SKIN_SLOT_COUNT);
    if (weaponBlock) {
      offset = weaponBlock.offset;
      skinWeaponAssault = weaponBlock.ids[0] || "";
      skinWeaponSniper = weaponBlock.ids[1] || "";
      skinWeaponPistol = weaponBlock.ids[2] || "";
      skinWeaponMp7 = weaponBlock.ids[3] || "";
    }
  }

  return {
    type: "pose",
    poseSeq,
    seq: poseSeq,
    characterModel,
    skinShirt,
    skinPants,
    skinBoots,
    skinGloves,
    skinFace,
    skinHair,
    skinWeaponAssault,
    skinWeaponSniper,
    skinWeaponPistol,
    skinWeaponMp7,
    position: { x, y, z },
    yaw,
    lookPitch,
    isCrouching: (flags & 1) !== 0,
    isSprinting: (flags & 2) !== 0,
    isDead: (flags & 4) !== 0,
    isHolstered: (flags & 8) !== 0,
    isGrounded: (flags & 16) !== 0,
    inputAuth: (flags & 32) !== 0,
    jumpPressed: (flags & 64) !== 0,
    shotHasEndPoint: (flags & 128) !== 0,
    isAiming: (flags & 256) !== 0,
    isSwimming: (flags & 512) !== 0,
    jumpState,
    weaponKind,
    weaponSlot0Kind,
    weaponSlot1Kind,
    activeWeaponSlot,
    activeWeaponMagAmmo,
    weaponPickupSeq,
    shotSeq,
    reloadSeq,
    hitPlayerSeq,
    footstepSeq,
    deathSeq,
    animSpeed,
    animPhase,
    wallAvoidBlend,
    moveInputX,
    moveInputZ,
    deathFallDirX,
    deathFallDirY,
    deathFallDirZ,
    shotOriginX,
    shotOriginY,
    shotOriginZ,
    shotDirX,
    shotDirY,
    shotDirZ,
    shotEndX,
    shotEndY,
    shotEndZ,
  };
}

function isBinaryPose(buffer) {
  return Buffer.isBuffer(buffer) &&
    buffer.length >= 4 &&
    buffer[0] === 0x52 &&
    buffer[1] === 0x54 &&
    buffer[2] === 0x50 &&
    buffer[3] === 0x31;
}

module.exports = {
  decodeBinaryPose,
  isBinaryPose,
};
