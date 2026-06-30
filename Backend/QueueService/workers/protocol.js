"use strict";

const MasterToWorker = {
  CREATE_MATCH: "CREATE_MATCH",
  ADD_PLAYERS: "ADD_PLAYERS",
  PLAYER_EVENT: "PLAYER_EVENT",
  PLAYER_DISCONNECT: "PLAYER_DISCONNECT",
  SHUTDOWN: "SHUTDOWN",
};

const WorkerToMaster = {
  WORKER_READY: "WORKER_READY",
  MATCH_CREATED: "MATCH_CREATED",
  MATCH_FINISHED: "MATCH_FINISHED",
  SNAPSHOT: "SNAPSHOT",
  MATCH_STATE: "MATCH_STATE",
  PLAYER_MESSAGE: "PLAYER_MESSAGE",
  METRICS: "METRICS",
  ERROR: "ERROR",
};

const PlayerEventType = {
  JOIN: "join",
  POSE: "pose",
  SHOT: "shot",
  HIT: "hit",
  WEAPON_PICK: "weapon_pick",
  PING: "ping",
};

module.exports = {
  MasterToWorker,
  WorkerToMaster,
  PlayerEventType,
};
