"use strict";

const DM_RESPAWN_DEBUG =
  process.env.DM_RESPAWN_DEBUG === "1" || process.env.DM_RESPAWN_DEBUG === "true";
const DM_RESPAWN_TRACE =
  process.env.DM_RESPAWN_TRACE === "1" || process.env.DM_RESPAWN_TRACE === "true" || DM_RESPAWN_DEBUG;

function logDmRespawn(event, payload) {
  if (!DM_RESPAWN_TRACE) {
    return;
  }
  console.log(`[DMRespawn] ${event}`, JSON.stringify(payload));
}

function clamp(value, min, max) {
  return Math.max(min, Math.min(max, value));
}

function normalizePoseMessage(message) {
  if (!message || typeof message !== "object") {
    return null;
  }

  const pos = message.position && typeof message.position === "object"
    ? message.position
    : message;

  const x = Number(pos.x);
  const y = Number(pos.y);
  const z = Number(pos.z);
  if (!Number.isFinite(x) || !Number.isFinite(y) || !Number.isFinite(z)) {
    return null;
  }

  return {
    x,
    y,
    z,
    yaw: Number(message.yaw),
    lookPitch: Number(message.lookPitch),
    velX: Number(message.velX ?? 0),
    velY: Number(message.velY ?? 0),
    velZ: Number(message.velZ ?? 0),
    isGrounded: message.isGrounded !== false,
    isDead: !!message.isDead,
    deathSeq: Number.isFinite(Number(message.deathSeq)) ? Number(message.deathSeq) : -1,
    seq: Number(message.seq ?? message.poseSeq ?? -1),
    characterModel: message.characterModel,
    skinShirt: message.skinShirt,
    skinPants: message.skinPants,
    skinBoots: message.skinBoots,
    skinGloves: message.skinGloves,
    skinFace: message.skinFace,
    skinHair: message.skinHair,
    skinWeaponAssault: message.skinWeaponAssault,
    skinWeaponSniper: message.skinWeaponSniper,
    skinWeaponPistol: message.skinWeaponPistol,
    skinWeaponMp7: message.skinWeaponMp7,
    isCrouching: message.isCrouching,
    isSprinting: message.isSprinting,
    isSwimming: message.isSwimming,
    isAiming: message.isAiming,
    isHolstered: message.isHolstered,
    jumpState: message.jumpState,
    animSpeed: message.animSpeed,
    animPhase: message.animPhase,
    wallAvoidBlend: message.wallAvoidBlend,
    moveInputX: message.moveInputX,
    moveInputZ: message.moveInputZ,
    weaponKind: message.weaponKind,
    weaponSlot0Kind: message.weaponSlot0Kind,
    weaponSlot1Kind: message.weaponSlot1Kind,
    activeWeaponSlot: message.activeWeaponSlot,
    shotSeq: message.shotSeq,
    reloadSeq: message.reloadSeq,
    hitPlayerSeq: message.hitPlayerSeq,
    footstepSeq: message.footstepSeq,
    shotOriginX: message.shotOriginX,
    shotOriginY: message.shotOriginY,
    shotOriginZ: message.shotOriginZ,
    shotDirX: message.shotDirX,
    shotDirY: message.shotDirY,
    shotDirZ: message.shotDirZ,
    shotEndX: message.shotEndX,
    shotEndY: message.shotEndY,
    shotEndZ: message.shotEndZ,
    shotHasEndPoint: message.shotHasEndPoint,
  };
}

function applyPresencePose(player, message, nowMs) {
  const pose = normalizePoseMessage(message);
  if (!pose) {
    return { ok: false, reason: "invalid_pose" };
  }

  player.applyNetworkPose(pose);
  player.x = pose.x;
  player.y = pose.y;
  player.z = pose.z;
  player.yaw = Number.isFinite(pose.yaw) ? pose.yaw : player.yaw;
  player.lookPitch = Number.isFinite(pose.lookPitch) ? pose.lookPitch : player.lookPitch;
  player.hasPose = true;
  player.lastPoseMs = nowMs;
  player.recordStateSample();
  return { ok: true };
}

function applyPose(player, message, limits, nowMs) {
  const pose = normalizePoseMessage(message);
  if (!pose) {
    return { ok: false, reason: "invalid_pose" };
  }

  const seq = pose.seq;
  const poseDeathSeq = pose.deathSeq;
  const serverDeathSeq = Math.max(0, Number(player.deathSeq) || 0);
  const graceActive = Number(player.respawnPoseGraceUntilMs) > nowMs;

  if (player.alive && pose.isDead) {
    logDmRespawn("applyPose.warn", {
      ticketId: player.ticketId,
      reason: "alive_player_dead_pose_flag",
      seq,
      lastSeq: player.lastSeq,
      poseDeathSeq: pose.deathSeq,
      serverDeathSeq: player.deathSeq,
      position: { x: pose.x, y: pose.y, z: pose.z },
      serverPosition: { x: player.x, y: player.y, z: player.z },
    });
  } else if (!player.alive && !pose.isDead) {
    logDmRespawn("applyPose.warn", {
      ticketId: player.ticketId,
      reason: "dead_player_alive_pose_flag",
      seq,
      lastSeq: player.lastSeq,
      poseDeathSeq: pose.deathSeq,
      serverDeathSeq: player.deathSeq,
      position: { x: pose.x, y: pose.y, z: pose.z },
      serverPosition: { x: player.x, y: player.y, z: player.z },
    });
  }

  // After respawn the server bumps deathSeq; drop poses queued before that (high seq, old deathSeq).
  if (poseDeathSeq >= 0 && serverDeathSeq > 0 && poseDeathSeq < serverDeathSeq) {
    logDmRespawn("applyPose.reject", {
      ticketId: player.ticketId,
      alive: player.alive,
      seq,
      lastSeq: player.lastSeq,
      poseDeathSeq,
      serverDeathSeq,
      reason: "stale_death_seq",
      position: { x: pose.x, y: pose.y, z: pose.z },
      serverPosition: { x: player.x, y: player.y, z: player.z },
      graceActive,
    });
    return { ok: false, reason: "stale_death_seq" };
  }

  // Client resets poseSeq to 0 on respawn; ignore leftover high-seq acceptance during grace.
  if (
    graceActive &&
    seq >= 0 &&
    seq <= 128 &&
    player.lastSeq !== null &&
    player.lastSeq !== undefined &&
    seq <= player.lastSeq
  ) {
    logDmRespawn("applyPose.seq_reset", {
      ticketId: player.ticketId,
      seq,
      lastSeq: player.lastSeq,
      poseDeathSeq,
      serverDeathSeq,
    });
    player.lastSeq = null;
  }

  if (player.lastSeq !== null && player.lastSeq !== undefined && seq >= 0 && seq <= player.lastSeq) {
    logDmRespawn("applyPose.reject", {
      ticketId: player.ticketId,
      alive: player.alive,
      seq,
      lastSeq: player.lastSeq,
      poseDeathSeq: pose.deathSeq,
      serverDeathSeq: player.deathSeq,
      reason: "out_of_order",
      position: { x: pose.x, y: pose.y, z: pose.z },
      serverPosition: { x: player.x, y: player.y, z: player.z },
    });
    return { ok: false, reason: "out_of_order" };
  }

  const dx = pose.x - player.x;
  const dy = pose.y - player.y;
  const dz = pose.z - player.z;
  const dist = Math.sqrt(dx * dx + dy * dy + dz * dz);
  const dtSec = player.lastPoseMs > 0 ? Math.max(0.001, (nowMs - player.lastPoseMs) / 1000) : 1 / 60;
  const speed = dist / dtSec;

  if (!graceActive && player.lastPoseMs > 0 && dist > limits.maxTeleportDistance) {
    player.cheatFlags |= 4;
    logDmRespawn("applyPose.reject", {
      ticketId: player.ticketId,
      alive: player.alive,
      seq,
      lastSeq: player.lastSeq,
      poseDeathSeq: pose.deathSeq,
      serverDeathSeq: player.deathSeq,
      reason: "teleport",
      dist,
      serverPosition: { x: player.x, y: player.y, z: player.z },
      posePosition: { x: pose.x, y: pose.y, z: pose.z },
    });
    return { ok: false, reason: "teleport", rejected: true };
  }

  if (!graceActive && player.lastPoseMs > 0 && speed > limits.maxPlayerSpeed * 1.35) {
    player.cheatFlags |= 8;
    logDmRespawn("applyPose.reject", {
      ticketId: player.ticketId,
      alive: player.alive,
      seq,
      lastSeq: player.lastSeq,
      poseDeathSeq: pose.deathSeq,
      serverDeathSeq: player.deathSeq,
      reason: "speed",
      speed,
    });
    return { ok: false, reason: "speed", rejected: true };
  }

  player.applyNetworkPose(pose);
  if (seq >= 0) {
    player.lastSeq = seq;
  }

  player.x = pose.x;
  player.y = pose.y;
  player.z = pose.z;
  player.hasPose = true;
  player.yaw = Number.isFinite(pose.yaw) ? pose.yaw : player.yaw;
  player.lookPitch = Number.isFinite(pose.lookPitch) ? pose.lookPitch : player.lookPitch;
  player.velX = pose.velX;
  player.velY = pose.velY;
  player.velZ = pose.velZ;
  player.isGrounded = pose.isGrounded;
  player.lastPoseMs = nowMs;
  player.recordStateSample();

  if (graceActive) {
    player.respawnPoseGraceUntilMs = 0;
  }

  logDmRespawn("applyPose.accept", {
    ticketId: player.ticketId,
    alive: player.alive,
    seq,
    lastSeq: player.lastSeq,
    poseDeathSeq: pose.deathSeq,
    serverDeathSeq: player.deathSeq,
    poseIsDead: pose.isDead,
    position: { x: player.x, y: player.y, z: player.z },
    graceActive,
    sampleTick: player.poseSampleSeq,
  });

  return { ok: true };
}

function tickMovement(player, dtSec) {
  if (!player.alive) {
    return;
  }
  player.x += player.velX * dtSec;
  player.y += player.velY * dtSec;
  player.z += player.velZ * dtSec;
}

module.exports = {
  applyPose,
  applyPresencePose,
  normalizePoseMessage,
  tickMovement,
  clamp,
};
