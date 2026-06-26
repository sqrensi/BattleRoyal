"use strict";

const SNAPSHOT_MAGIC = 0x53534150; // SSAP

function writeF32(buffer, offset, value) {
  buffer.writeFloatLE(value, offset);
  return offset + 4;
}

function encodeSnapshotBinary(frame) {
  const players = frame.players || [];
  const buffer = Buffer.allocUnsafe(16 + players.length * 48);
  let o = 0;
  buffer.writeUInt32LE(SNAPSHOT_MAGIC, o);
  o += 4;
  buffer.writeUInt32LE(frame.tick >>> 0, o);
  o += 4;
  buffer.writeUInt8(players.length & 0xff, o);
  o += 1;
  buffer.writeUInt8(String(frame.phase || "").charCodeAt(0) || 0, o);
  o += 1;
  buffer.writeUInt16LE(frame.round & 0xffff, o);
  o += 2;
  o += 4; // reserved

  for (const p of players) {
    o = writeF32(buffer, o, p.x || 0);
    o = writeF32(buffer, o, p.y || 0);
    o = writeF32(buffer, o, p.z || 0);
    o = writeF32(buffer, o, p.yaw || 0);
    o = writeF32(buffer, o, p.velX || 0);
    o = writeF32(buffer, o, p.velY || 0);
    o = writeF32(buffer, o, p.velZ || 0);
    buffer.writeUInt16LE(Math.max(0, Math.min(65535, Math.round(p.hp || 0))), o);
    o += 2;
    buffer.writeUInt8(p.alive ? 1 : 0, o);
    o += 1;
    buffer.writeUInt8((p.weaponKind ?? 255) & 0xff, o);
    o += 1;
    buffer.writeUInt8(Math.max(0, Math.min(255, p.roundWins || 0)), o);
    o += 1;
    o += 8; // ticket hash placeholder
  }

  return buffer;
}

function decodeSnapshotBinary(buffer) {
  if (!Buffer.isBuffer(buffer) || buffer.length < 16) {
    return null;
  }
  if (buffer.readUInt32LE(0) !== SNAPSHOT_MAGIC) {
    return null;
  }
  const tick = buffer.readUInt32LE(4);
  const count = buffer.readUInt8(8);
  const phaseCode = buffer.readUInt8(9);
  const round = buffer.readUInt16LE(10);
  return { tick, count, phaseCode, round };
}

module.exports = {
  SNAPSHOT_MAGIC,
  encodeSnapshotBinary,
  decodeSnapshotBinary,
};
