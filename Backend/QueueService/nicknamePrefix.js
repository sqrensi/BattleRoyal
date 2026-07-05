"use strict";

const PRO_TOP_COUNT = 5;
const VIP_DURATION_MS = 30 * 24 * 60 * 60 * 1000;

function normalizePrefix(prefix) {
  const value = String(prefix || "").trim().toUpperCase();
  if (value === "LEGEND" || value === "PRO" || value === "VIP") {
    return value;
  }

  if (value === "VIP-LEGEND" || value === "VIP-PRO") {
    return value;
  }

  return "";
}

function isVipPrefixActive({ vipPrefixExpiresAtMs, nowMs = Date.now() }) {
  const expiresAt = Math.floor(Number(vipPrefixExpiresAtMs) || 0);
  return expiresAt > Math.floor(Number(nowMs) || 0);
}

function computeVipPrefixExpiresAt(currentExpiresAtMs, nowMs = Date.now()) {
  const now = Math.floor(Number(nowMs) || 0);
  const current = Math.floor(Number(currentExpiresAtMs) || 0);
  const base = Math.max(now, current);
  return base + VIP_DURATION_MS;
}

function resolveRankPrefix(leaderboardRank) {
  const rank = Math.floor(Number(leaderboardRank) || 0);
  if (rank === 1) {
    return "LEGEND";
  }

  if (rank >= 2 && rank <= PRO_TOP_COUNT) {
    return "PRO";
  }

  return "";
}

function combinePrefixes(rankPrefix, vipPrefixExpiresAtMs, nowMs = Date.now()) {
  const normalizedRank = normalizePrefix(rankPrefix) || "";
  const rankValue =
    normalizedRank === "LEGEND" || normalizedRank === "PRO" ? normalizedRank : "";
  const vipActive = isVipPrefixActive({ vipPrefixExpiresAtMs, nowMs });

  if (vipActive && rankValue) {
    return `VIP-${rankValue}`;
  }

  if (vipActive) {
    return "VIP";
  }

  return rankValue;
}

function resolveNicknamePrefix({ leaderboardRank, vipPrefixExpiresAtMs, nowMs = Date.now() }) {
  return combinePrefixes(resolveRankPrefix(leaderboardRank), vipPrefixExpiresAtMs, nowMs);
}

function resolveBestNicknamePrefix(ranks, vipPrefixExpiresAtMs, nowMs = Date.now()) {
  const normalizedRanks = Array.isArray(ranks)
    ? ranks.map((rank) => Math.floor(Number(rank) || 0)).filter((rank) => rank > 0)
    : [];

  let rankPrefix = "";
  if (normalizedRanks.some((rank) => rank === 1)) {
    rankPrefix = "LEGEND";
  } else if (normalizedRanks.some((rank) => rank >= 2 && rank <= PRO_TOP_COUNT)) {
    rankPrefix = "PRO";
  }

  return combinePrefixes(rankPrefix, vipPrefixExpiresAtMs, nowMs);
}

function formatNicknameWithPrefix(prefix, nickname) {
  const nick = String(nickname || "Игрок").trim() || "Игрок";
  const normalized = normalizePrefix(prefix);
  if (!normalized) {
    return nick;
  }

  return `[${normalized}] ${nick}`;
}

module.exports = {
  PRO_TOP_COUNT,
  VIP_DURATION_MS,
  normalizePrefix,
  isVipPrefixActive,
  computeVipPrefixExpiresAt,
  resolveRankPrefix,
  combinePrefixes,
  resolveNicknamePrefix,
  resolveBestNicknamePrefix,
  formatNicknameWithPrefix,
};
