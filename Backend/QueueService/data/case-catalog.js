const catalog = require("./case-catalog.json");

const SHOP_CASES = Array.isArray(catalog.shopCases) ? catalog.shopCases : [];

const CASE_BY_ID = {};
for (let i = 0; i < SHOP_CASES.length; i++) {
  const entry = SHOP_CASES[i];
  if (!entry || !entry.caseId) {
    continue;
  }

  CASE_BY_ID[String(entry.caseId).trim()] = normalizeCaseEntry(entry);
}

function normalizeCaseEntry(raw) {
  const lootPool = Array.isArray(raw.lootPool)
    ? raw.lootPool.map((skinId) => String(skinId || "").trim()).filter(Boolean)
    : [];

  return {
    caseId: String(raw.caseId || "").trim(),
    displayName: String(raw.displayName || raw.caseId || "Case").trim(),
    pictureFolder: String(raw.pictureFolder || "001").trim(),
    price: typeof raw.price === "number" ? Math.max(0, raw.price) : 0,
    lootPool,
  };
}

function getCaseDefinition(caseId) {
  const normalized = String(caseId || "").trim();
  return normalized ? CASE_BY_ID[normalized] || null : null;
}

function getShopCases() {
  return SHOP_CASES.map((entry) => normalizeCaseEntry(entry));
}

function getCasePrice(caseId) {
  const definition = getCaseDefinition(caseId);
  return definition ? definition.price : 0;
}

function rollCaseLoot(caseId) {
  const definition = getCaseDefinition(caseId);
  if (!definition || definition.lootPool.length === 0) {
    return "";
  }

  const index = Math.floor(Math.random() * definition.lootPool.length);
  return definition.lootPool[index];
}

module.exports = {
  SHOP_CASES,
  getCaseDefinition,
  getShopCases,
  getCasePrice,
  rollCaseLoot,
};
