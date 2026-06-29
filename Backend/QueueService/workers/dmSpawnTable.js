"use strict";

// World spawn poses for dm scene (SpawnPoints root), sorted by child name.
const DM_SPAWNS = [
  { x: -23.6, y: 0, z: 25.06, yaw: 0 },
  { x: -1.29, y: 0, z: -0.24, yaw: 0 },
  { x: -19.44, y: 0, z: 5.71, yaw: 0 },
  { x: -2.7, y: 0, z: 23.1, yaw: 0 },
  { x: 15.9, y: 0, z: 20.1, yaw: 0 },
  { x: 15.18, y: 0, z: -2.04, yaw: 0 },
  { x: -2.16, y: 0, z: -7.45, yaw: 0 },
  { x: -4.06, y: 0, z: -23.23, yaw: 0 },
  { x: -15.87, y: 0, z: -18.7, yaw: 0 },
  { x: -23.56, y: 0, z: -7.15, yaw: 0 },
  { x: 20.1, y: 0, z: -20.82, yaw: 0 },
  { x: 20.1, y: 6.61, z: -10.24, yaw: 0 },
  { x: 21.1, y: 6.61, z: 0.73, yaw: 0 },
];

function getSpawnCount() {
  return DM_SPAWNS.length;
}

function resolveSpawnPose(slotIndex) {
  if (!DM_SPAWNS.length) {
    return null;
  }

  const slot = Math.max(0, Math.min(DM_SPAWNS.length - 1, Math.floor(Number(slotIndex) || 0)));
  return DM_SPAWNS[slot];
}

function rollRandomSpawnSlot() {
  return Math.floor(Math.random() * DM_SPAWNS.length);
}

module.exports = {
  DM_SPAWNS,
  getSpawnCount,
  resolveSpawnPose,
  rollRandomSpawnSlot,
};
