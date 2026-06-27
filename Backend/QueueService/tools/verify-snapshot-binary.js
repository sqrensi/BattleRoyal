#!/usr/bin/env node
"use strict";

const { encodeSnapshotRts1, VERSION } = require("../network/snapshotBinary");

function readU8(buf, o) {
  return buf[o.i++];
}

function readU16(buf, o) {
  const v = buf.readUInt16LE(o.i);
  o.i += 2;
  return v;
}

function readU32(buf, o) {
  const v = buf.readUInt32LE(o.i);
  o.i += 4;
  return v;
}

function readF32(buf, o) {
  const v = buf.readFloatLE(o.i);
  o.i += 4;
  return v;
}

function readUtf8(buf, o) {
  const len = readU8(buf, o);
  const v = buf.toString("utf8", o.i, o.i + len);
  o.i += len;
  return v;
}

// Mirrors RealtimeSnapshotBinaryCodec.cs v12 decode.
function tryDecodeLikeClient(data) {
  if (!data || data.length < 12 || data.toString("ascii", 0, 4) !== "RTS1") {
    return { ok: false, reason: "bad magic" };
  }

  const o = { i: 4 };
  const version = readU8(data, o);
  if (version !== VERSION) {
    return { ok: false, reason: `version ${version} != ${VERSION}` };
  }

  readU32(data, o);
  readU16(data, o);
  if (version >= 13) {
    readU16(data, o);
  }
  const flags = readU8(data, o);
  if (flags & 1) {
    readU32(data, o);
    readF32(data, o);
    readF32(data, o);
    readF32(data, o);
    readF32(data, o);
  }

  const playerCount = readU8(data, o);
  const tickets = [];
  for (let p = 0; p < playerCount; p++) {
    const ticketId = readUtf8(data, o);
    readUtf8(data, o);
    for (let s = 0; s < 10; s++) {
      readUtf8(data, o);
    }

    readU32(data, o);
    readF32(data, o);
    readF32(data, o);
    readF32(data, o);
    readF32(data, o);
    readF32(data, o);
    readF32(data, o);
    readF32(data, o);
    readU16(data, o);
    readU8(data, o);
    readF32(data, o);
    readU32(data, o);
    readU32(data, o);
    readU32(data, o);
    readU32(data, o);
    readF32(data, o);
    readF32(data, o);
    readF32(data, o);
    readU32(data, o);
    readF32(data, o);
    readF32(data, o);
    readF32(data, o);
    readF32(data, o);
    readF32(data, o);
    readF32(data, o);
    readF32(data, o);
    readF32(data, o);
    readF32(data, o);
    readF32(data, o);
    readF32(data, o);
    readF32(data, o);
    readF32(data, o);
    readF32(data, o);
    readU8(data, o);
    readU32(data, o);
    readF32(data, o);
    readU8(data, o);
    readU8(data, o);
    readU8(data, o);
    readU8(data, o);
    readU8(data, o);
    const ringCount = readU8(data, o);
    for (let r = 0; r < ringCount; r++) {
      readU32(data, o);
      for (let f = 0; f < 10; f++) {
        readF32(data, o);
      }
      readU8(data, o);
    }
    const historyCount = readU8(data, o);
    for (let h = 0; h < historyCount; h++) {
      readU32(data, o);
      for (let f = 0; f < 7; f++) {
        readF32(data, o);
      }
    }
    tickets.push(ticketId);
    if (o.i > data.length) {
      return { ok: false, reason: `overflow after player ${p}` };
    }
  }

  if (o.i !== data.length) {
    return { ok: false, reason: `trailing bytes ${data.length - o.i} at offset ${o.i}/${data.length}` };
  }

  return { ok: true, playerCount, tickets };
}

const frame = {
  serverTick: 42,
  serverTickRate: 30,
  movementSampleRateHz: 64,
  players: [
    {
      ticketId: "d61a495c-91b1-43ad-9f9e-c2936193e650",
      characterModel: "Ch18",
      skinShirt: "",
      skinPants: "",
      skinBoots: "",
      skinGloves: "",
      skinFace: "",
      skinHair: "",
      skinWeaponAssault: "",
      skinWeaponSniper: "",
      skinWeaponPistol: "",
      skinWeaponMp7: "",
      sampleTick: 42,
      position: { x: 39.05, y: 0.606, z: 288.21 },
      yaw: 0,
      velX: 0,
      velY: 0,
      velZ: 0,
      isDead: false,
      isGrounded: true,
      jumpState: 0,
      lookPitch: 0,
      shotSeq: 0,
      reloadSeq: 0,
      hitPlayerSeq: 0,
      footstepSeq: 0,
      wallAvoidBlend: 0,
      animSpeed: 0,
      animPhase: 0,
      deathSeq: 0,
      deathFallDirX: 0,
      deathFallDirY: 0,
      deathFallDirZ: 0,
      shotOriginX: 0,
      shotOriginY: 0,
      shotOriginZ: 0,
      shotDirX: 0,
      shotDirY: 0,
      shotDirZ: 0,
      moveInputX: 0,
      moveInputZ: 0,
      shotEndX: 0,
      shotEndY: 0,
      shotEndZ: 0,
      shotHasEndPoint: false,
      recentShots: [],
      weaponPickupSeq: 0,
      medkitRemainingSeconds: 0,
      medkitCount: 0,
      hasWeapon: false,
      weaponKind: 0,
      weaponSlot0Kind: 255,
      weaponSlot1Kind: 255,
      activeWeaponSlot: 255,
      history: [
        {
          sampleTick: 40,
          x: 39.0,
          y: 0.606,
          z: 288.0,
          yaw: 0,
          velX: 1.2,
          velY: 0,
          velZ: 0.8,
        },
        {
          sampleTick: 41,
          x: 39.05,
          y: 0.606,
          z: 288.21,
          yaw: 0,
          velX: 1.2,
          velY: 0,
          velZ: 0.8,
        },
      ],
    },
  ],
};

const encoded = encodeSnapshotRts1(frame);
const result = tryDecodeLikeClient(encoded);
if (!result.ok) {
  console.error("[verify-snapshot-binary] FAIL", result.reason, "len=", encoded.length);
  process.exit(1);
}

console.log(
  `[verify-snapshot-binary] OK version=${VERSION} bytes=${encoded.length} players=${result.playerCount} tickets=${result.tickets.join(",")}`
);
