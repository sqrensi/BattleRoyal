const catalog = require("./achievement-catalog.json");

const ACHIEVEMENTS = Array.isArray(catalog.achievements)
  ? catalog.achievements.map(normalizeAchievement).filter(Boolean)
  : [];

const ACHIEVEMENT_BY_ID = {};
const ACHIEVEMENTS_BY_EVENT = {};

for (let i = 0; i < ACHIEVEMENTS.length; i++) {
  const entry = ACHIEVEMENTS[i];
  ACHIEVEMENT_BY_ID[entry.achievementId] = entry;
  if (!ACHIEVEMENTS_BY_EVENT[entry.eventType]) {
    ACHIEVEMENTS_BY_EVENT[entry.eventType] = [];
  }
  ACHIEVEMENTS_BY_EVENT[entry.eventType].push(entry);
}

function normalizeAchievement(raw) {
  if (!raw || !raw.achievementId || !raw.eventType) {
    return null;
  }

  const reward = normalizeReward(raw.reward);
  if (!reward) {
    return null;
  }

  return {
    achievementId: String(raw.achievementId).trim(),
    code: String(raw.code || raw.achievementId).trim(),
    title: String(raw.title || raw.achievementId).trim(),
    description: String(raw.description || "").trim(),
    target: Math.max(1, Number(raw.target) || 1),
    sortOrder: Number.isFinite(raw.sortOrder) ? raw.sortOrder : 0,
    eventType: String(raw.eventType).trim(),
    reward,
  };
}

function normalizeReward(raw) {
  if (!raw || !raw.type) {
    return null;
  }

  const type = String(raw.type).trim().toLowerCase();
  if (type === "currency") {
    const amount = Math.max(1, Math.floor(Number(raw.amount) || 0));
    return { type: "currency", amount };
  }

  if (type === "case") {
    const caseId = String(raw.caseId || "").trim();
    const amount = Math.max(1, Math.floor(Number(raw.amount) || 1));
    if (!caseId) {
      return null;
    }
    return { type: "case", caseId, amount };
  }

  return null;
}

function getAchievementDefinition(achievementId) {
  const normalized = String(achievementId || "").trim();
  return normalized ? ACHIEVEMENT_BY_ID[normalized] || null : null;
}

function getAchievementsByEventType(eventType) {
  const normalized = String(eventType || "").trim();
  return normalized ? ACHIEVEMENTS_BY_EVENT[normalized] || [] : [];
}

function getAllAchievements() {
  return ACHIEVEMENTS.slice();
}

module.exports = {
  getAchievementDefinition,
  getAchievementsByEventType,
  getAllAchievements,
};
