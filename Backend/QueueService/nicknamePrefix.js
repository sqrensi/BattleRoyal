"use strict";

const PRO_TOP_COUNT = 5;
const VIP_DURATION_MS = 30 * 24 * 60 * 60 * 1000;

function formatRankPrefixWithCount(rankPrefix, count) {
  const base = String(rankPrefix || "").trim().toUpperCase();
  if (base !== "LEGEND" && base !== "PRO") {
    return "";
  }

  const normalizedCount = Math.floor(Number(count) || 0);
  if (normalizedCount <= 1) {
    return base;
  }

  return `${base}x${normalizedCount}`;
}

function parseRankPrefix(prefix) {
  const value = String(prefix || "").trim().toUpperCase();
  const match = value.match(/^(LEGEND|PRO)(?:X(\d+))?$/);
  if (!match) {
    return { base: "", count: 0 };
  }

  const base = match[1];
  const count = match[2] ? Math.max(1, Math.floor(Number(match[2]) || 0)) : 1;
  return { base, count };
}

function normalizePrefix(prefix) {
  const value = String(prefix || "").trim().toUpperCase();
  if (value === "VIP") {
    return "VIP";
  }

  const vipMatch = value.match(/^VIP-(LEGEND|PRO)(?:X(\d+))?$/);
  if (vipMatch) {
    const base = vipMatch[1];
    const count = vipMatch[2] ? Math.max(1, Math.floor(Number(vipMatch[2]) || 0)) : 1;
    return `VIP-${formatRankPrefixWithCount(base, count)}`;
  }

  const parsed = parseRankPrefix(value);
  if (parsed.base) {
    return formatRankPrefixWithCount(parsed.base, parsed.count);
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
  const parsed = parseRankPrefix(normalizePrefix(rankPrefix) || rankPrefix);
  const rankValue = parsed.base ? formatRankPrefixWithCount(parsed.base, parsed.count) : "";
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

  const legendCount = normalizedRanks.filter((rank) => rank === 1).length;
  const proCount = normalizedRanks.filter(
    (rank) => rank >= 2 && rank <= PRO_TOP_COUNT
  ).length;

  let rankPrefix = "";
  if (legendCount > 0) {
    rankPrefix = formatRankPrefixWithCount("LEGEND", legendCount);
  } else if (proCount > 0) {
    rankPrefix = formatRankPrefixWithCount("PRO", proCount);
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
  formatRankPrefixWithCount,
  parseRankPrefix,
  combinePrefixes,
  resolveNicknamePrefix,
  resolveBestNicknamePrefix,
  formatNicknameWithPrefix,
};
