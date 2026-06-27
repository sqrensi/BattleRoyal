"use strict";

const { encodeSnapshotRts1 } = require("../network/snapshotBinary");

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

  return {
    encoding: "binary",
    payload: encodeSnapshotRts1(frame),
  };
}

module.exports = {
  buildSnapshotFrame,
  buildMatchStatePayload,
  maybeEncodeBinary,
};
