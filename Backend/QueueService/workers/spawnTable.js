"use strict";

// World spawn poses for 1x1 scene (SpawnPoints1 / SpawnPoints2), sorted by child name.
const TEAM_SPAWNS = [
  [
    { x: 39.05, y: 0.606, z: 288.21, yaw: 0 },
    { x: 32.11, y: 0.606, z: 288.21, yaw: 0 },
    { x: 45.32, y: 0.606, z: 288.21, yaw: 0 },
  ],
  [
    { x: 38.25, y: 0.606, z: 314.69, yaw: 180 },
    { x: 30.75, y: 0.606, z: 314.69, yaw: 180 },
    { x: 45.94, y: 0.606, z: 314.69, yaw: 180 },
  ],
];

function getSpawnCount(teamIndex) {
  const team = TEAM_SPAWNS[teamIndex] || TEAM_SPAWNS[0];
  return team.length;
}

function resolveSpawnPose(teamIndex, slotIndex) {
  const team = TEAM_SPAWNS[Math.max(0, Math.min(TEAM_SPAWNS.length - 1, teamIndex))] || TEAM_SPAWNS[0];
  if (!team.length) {
    return null;
  }

  const slot = Math.max(0, Math.min(team.length - 1, Math.floor(Number(slotIndex) || 0)));
  return team[slot];
}

function getTeamCenter(teamIndex) {
  const team = TEAM_SPAWNS[Math.max(0, Math.min(TEAM_SPAWNS.length - 1, teamIndex))] || TEAM_SPAWNS[0];
  if (!team.length) {
    return { x: 39, z: 288 };
  }

  let sumX = 0;
  let sumZ = 0;
  for (const pose of team) {
    sumX += pose.x;
    sumZ += pose.z;
  }

  return {
    x: sumX / team.length,
    z: sumZ / team.length,
  };
}

function getArenaBounds() {
  let minX = Infinity;
  let maxX = -Infinity;
  let minZ = Infinity;
  let maxZ = -Infinity;

  for (const team of TEAM_SPAWNS) {
    for (const pose of team) {
      minX = Math.min(minX, pose.x);
      maxX = Math.max(maxX, pose.x);
      minZ = Math.min(minZ, pose.z);
      maxZ = Math.max(maxZ, pose.z);
    }
  }

  const padX = 10;
  const padZ = 12;
  return {
    minX: minX - padX,
    maxX: maxX + padX,
    minZ: minZ - padZ,
    maxZ: maxZ + padZ,
  };
}

module.exports = {
  TEAM_SPAWNS,
  getSpawnCount,
  resolveSpawnPose,
  getTeamCenter,
  getArenaBounds,
};
