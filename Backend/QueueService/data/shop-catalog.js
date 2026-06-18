const catalog = require("./skin-catalog.json");

const SHOP_BASE_PRICE = 500;
const DEFAULT_OWNED_SKIN_IDS = catalog.defaultOwnedSkinIds || [];
const DEFAULT_EQUIPPED = normalizeDefaultEquipped(catalog.defaultEquipped || {});

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

const SHOP_PRICE_BY_SKIN_ID = buildShopPrices(catalog.shopItems || []);

function normalizeDefaultEquipped(raw) {
  return {
    shirt: raw.shirt || "",
    pants: raw.pants || "",
    boots: raw.boots || "",
    gloves: raw.gloves || "",
    face: raw.face || "",
    hair: raw.hair || "",
    weapon_assault: raw.weapon_assault || raw.weaponAssault || "",
    weapon_sniper: raw.weapon_sniper || raw.weaponSniper || "",
    weapon_pistol: raw.weapon_pistol || raw.weaponPistol || "",
    weapon_mp7: raw.weapon_mp7 || raw.weaponMp7 || "",
  };
}

function buildShopPrices(shopItems) {
  const prices = {};
  for (let i = 0; i < shopItems.length; i++) {
    const entry = shopItems[i];
    if (!entry || !entry.skinId) {
      continue;
    }
    prices[String(entry.skinId).trim()] =
      typeof entry.price === "number" ? entry.price : SHOP_BASE_PRICE;
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
  if (Object.prototype.hasOwnProperty.call(SHOP_PRICE_BY_SKIN_ID, normalized)) {
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
