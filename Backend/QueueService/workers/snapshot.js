"use strict";

const { encodeSnapshotBinary } = require("../network/packets");

function buildSnapshotFrame(matchId, tick, players, phase, round) {
  return {
    type: "snapshot",
    matchId,
    tick,
    phase,
    round,
    players: players.map((p) => p.toSnapshot()),
  };
}

function buildMatchStatePayload(matchId, duelState) {
  return {
    type: "match_state",
    matchId,
    phase: duelState.phase,
    round: duelState.round,
    phaseEndsAtMs: duelState.timerEndsAtMs,
    roundWins: duelState.roundWins,
    winnerTicketId: duelState.winnerTicketId || "",
  };
}

function maybeEncodeBinary(frame, useBinary) {
  if (!useBinary) {
    return { encoding: "json", payload: frame };
  }
  return { encoding: "binary", payload: encodeSnapshotBinary(frame) };
}

module.exports = {
  buildSnapshotFrame,
  buildMatchStatePayload,
  maybeEncodeBinary,
};
