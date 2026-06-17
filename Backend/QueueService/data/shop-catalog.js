const SHOP_BASE_PRICE = 500;
const SHOP_PRICE_STEP = 250;

const DEFAULT_OWNED_SKIN_IDS = [
  "tshirts_001",
  "pants_001",
  "shoes_001",
  "gloves_001",
  "attachment_face_001",
  "attachment_hair_003",
  "weapon_ak47_000",
  "weapon_sniper_000",
  "weapon_pistol_000",
  "weapon_mp7_000",
];

const DEFAULT_EQUIPPED = {
  shirt: "tshirts_001",
  pants: "pants_001",
  boots: "shoes_001",
  gloves: "gloves_001",
  face: "attachment_face_001",
  hair: "attachment_hair_003",
  weapon_assault: "weapon_ak47_000",
  weapon_sniper: "weapon_sniper_000",
  weapon_pistol: "weapon_pistol_000",
  weapon_mp7: "weapon_mp7_000",
};

const EQUIPMENT_SLOTS = [
  "shirt",
  "pants",
  "boots",
  "gloves",
  "face",
  "hair",
  "weapon_assault",
  "weapon_sniper",
  "weapon_pistol",
  "weapon_mp7",
];

const SKIN_ID_PATTERN =
  /^(tshirts|pants|shoes|gloves)_\d{3}$|^attachment_(face|hair)_\d{3}$|^weapon_(ak47|sniper|pistol|mp7)_\d{3}$/;

const SHOP_SKIN_IDS = [
  "tshirts_002",
  "tshirts_003",
  "pants_002",
  "pants_003",
  "shoes_002",
  "shoes_003",
  "gloves_002",
  "gloves_003",
  "attachment_face_002",
  "attachment_face_003",
  "attachment_hair_001",
  "attachment_hair_002",
  "weapon_ak47_001",
  "weapon_sniper_001",
  "weapon_pistol_001",
  "weapon_mp7_001",
];

const SHOP_PRICE_BY_SKIN_ID = buildShopPrices();

function buildShopPrices() {
  const prices = {};
  for (let i = 0; i < SHOP_SKIN_IDS.length; i++) {
    prices[SHOP_SKIN_IDS[i]] = SHOP_BASE_PRICE + i * SHOP_PRICE_STEP;
  }
  return prices;
}

function isKnownSkinId(skinId) {
  if (typeof skinId !== "string" || !skinId.trim()) {
    return false;
  }
  const normalized = skinId.trim();
  if (DEFAULT_OWNED_SKIN_IDS.includes(normalized)) {
    return true;
  }
  if (SHOP_SKIN_IDS.includes(normalized)) {
    return true;
  }
  return SKIN_ID_PATTERN.test(normalized);
}

function isDefaultOwnedSkinId(skinId) {
  return DEFAULT_OWNED_SKIN_IDS.includes(String(skinId || "").trim());
}

function getShopPrice(skinId) {
  const normalized = String(skinId || "").trim();
  if (!normalized || isDefaultOwnedSkinId(normalized)) {
    return 0;
  }
  return SHOP_PRICE_BY_SKIN_ID[normalized] || SHOP_BASE_PRICE;
}

function slotForSkinId(skinId) {
  const normalized = String(skinId || "").trim();
  if (normalized.startsWith("tshirts_")) return "shirt";
  if (normalized.startsWith("pants_")) return "pants";
  if (normalized.startsWith("shoes_")) return "boots";
  if (normalized.startsWith("gloves_")) return "gloves";
  if (normalized.startsWith("attachment_face_")) return "face";
  if (normalized.startsWith("attachment_hair_")) return "hair";
  if (normalized.startsWith("weapon_ak47_")) return "weapon_assault";
  if (normalized.startsWith("weapon_sniper_")) return "weapon_sniper";
  if (normalized.startsWith("weapon_pistol_")) return "weapon_pistol";
  if (normalized.startsWith("weapon_mp7_")) return "weapon_mp7";
  return "";
}

module.exports = {
  SHOP_BASE_PRICE,
  DEFAULT_OWNED_SKIN_IDS,
  DEFAULT_EQUIPPED,
  EQUIPMENT_SLOTS,
  isKnownSkinId,
  isDefaultOwnedSkinId,
  getShopPrice,
  slotForSkinId,
};
