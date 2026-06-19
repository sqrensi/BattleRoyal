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

function normalizeLootPool(rawLootPool) {
  if (!Array.isArray(rawLootPool)) {
    return [];
  }

  const lootPool = [];
  for (let i = 0; i < rawLootPool.length; i++) {
    const entry = rawLootPool[i];
    if (!entry) {
      continue;
    }

    const skinId = String(entry.skinId || entry || "").trim();
    if (!skinId) {
      continue;
    }

    const weight =
      typeof entry.weight === "number" && entry.weight > 0 ? entry.weight : 1;
    lootPool.push({ skinId, weight });
  }

  return lootPool;
}

function normalizeCaseEntry(raw) {
  return {
    caseId: String(raw.caseId || "").trim(),
    displayName: String(raw.displayName || raw.caseId || "Case").trim(),
    pictureFolder: String(raw.pictureFolder || "001").trim(),
    price: typeof raw.price === "number" ? Math.max(0, raw.price) : 0,
    lootPool: normalizeLootPool(raw.lootPool),
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

  let totalWeight = 0;
  for (let i = 0; i < definition.lootPool.length; i++) {
    totalWeight += definition.lootPool[i].weight;
  }

  if (totalWeight <= 0) {
    return "";
  }

  let roll = Math.floor(Math.random() * totalWeight);
  for (let i = 0; i < definition.lootPool.length; i++) {
    roll -= definition.lootPool[i].weight;
    if (roll < 0) {
      return definition.lootPool[i].skinId;
    }
  }

  return definition.lootPool[definition.lootPool.length - 1].skinId;
}

module.exports = {
  SHOP_CASES,
  getCaseDefinition,
  getShopCases,
  getCasePrice,
  rollCaseLoot,
};
