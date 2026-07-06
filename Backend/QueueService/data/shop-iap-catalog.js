const catalog = require("./shop-iap-catalog.json");

const PRODUCTS = Array.isArray(catalog.products) ? catalog.products.map(normalizeProduct).filter(Boolean) : [];
const PRODUCT_BY_ID = {};

for (let i = 0; i < PRODUCTS.length; i++) {
  PRODUCT_BY_ID[PRODUCTS[i].productId] = PRODUCTS[i];
}

function normalizeProduct(raw) {
  if (!raw || !raw.productId) {
    return null;
  }

  const rewardType = String(raw.rewardType || "").trim().toLowerCase();
  if (rewardType === "currency") {
    return {
      productId: String(raw.productId).trim(),
      displayName: String(raw.displayName || raw.productId).trim(),
      priceRubles: Math.max(0, Number(raw.priceRubles) || 0),
      rewardType: "currency",
      amount: Math.max(1, Math.floor(Number(raw.amount) || 0)),
      caseId: "",
    };
  }

  if (rewardType === "case") {
    const caseId = String(raw.caseId || "").trim();
    if (!caseId) {
      return null;
    }

    return {
      productId: String(raw.productId).trim(),
      displayName: String(raw.displayName || raw.productId).trim(),
      priceRubles: Math.max(0, Number(raw.priceRubles) || 0),
      rewardType: "case",
      amount: Math.max(1, Math.floor(Number(raw.amount) || 1)),
      caseId,
    };
  }

  if (rewardType === "vip_prefix") {
    return {
      productId: String(raw.productId).trim(),
      displayName: String(raw.displayName || raw.productId).trim(),
      priceRubles: Math.max(0, Number(raw.priceRubles) || 0),
      rewardType: "vip_prefix",
      amount: 1,
      caseId: "",
    };
  }

  if (rewardType === "no_ads") {
    return {
      productId: String(raw.productId).trim(),
      displayName: String(raw.displayName || raw.productId).trim(),
      priceRubles: Math.max(0, Number(raw.priceRubles) || 0),
      rewardType: "no_ads",
      amount: 1,
      caseId: "",
    };
  }

  return null;
}

function getIapProduct(productId) {
  const normalized = String(productId || "").trim();
  return normalized ? PRODUCT_BY_ID[normalized] || null : null;
}

module.exports = {
  PRODUCTS,
  getIapProduct,
};
