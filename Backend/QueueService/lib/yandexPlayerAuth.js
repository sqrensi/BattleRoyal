"use strict";

const crypto = require("crypto");

function isYandexExternalPlayerId(playerId) {
  return String(playerId || "")
    .trim()
    .toLowerCase()
    .startsWith("yg-");
}

function getSecretKey() {
  return String(process.env.YANDEX_GAMES_SECRET_KEY || "").trim();
}

function isEnforced() {
  const flag = String(process.env.YANDEX_GAMES_AUTH_REQUIRED || "").trim().toLowerCase();
  if (flag === "0" || flag === "false" || flag === "no") {
    return false;
  }
  if (flag === "1" || flag === "true" || flag === "yes") {
    return true;
  }
  return !!getSecretKey();
}

function extractSignature(req, body) {
  const headerValue =
    (req && req.headers && (req.headers["x-yandex-player-signature"] || req.headers["X-Yandex-Player-Signature"])) ||
    "";
  const header = String(headerValue || "").trim();
  if (header) {
    return header;
  }

  if (body && body.yandexSignature) {
    return String(body.yandexSignature).trim();
  }

  return "";
}

function extractUniqueId(payload) {
  const data = payload && typeof payload.data === "object" ? payload.data : payload;
  if (!data || typeof data !== "object") {
    return "";
  }

  return String(
    data.uniqueID ||
      data.uniqueId ||
      data.unique_id ||
      data.playerUniqueID ||
      data.playerUniqueId ||
      ""
  ).trim();
}

function buildExpectedExternalId(uniqueId) {
  const normalized = String(uniqueId || "").trim();
  if (!normalized) {
    return "";
  }

  return normalized.toLowerCase().startsWith("yg-") ? normalized : `yg-${normalized}`;
}

function verifyYandexSignature(signature, secretKey) {
  if (!signature) {
    return { ok: false, error: "MissingYandexSignature", message: "Yandex player signature is required." };
  }

  if (!secretKey) {
    return {
      ok: false,
      error: "AuthNotConfigured",
      message: "Yandex auth verification is not configured on server.",
    };
  }

  const parts = String(signature).split(".");
  if (parts.length !== 2 || !parts[0] || !parts[1]) {
    return { ok: false, error: "InvalidSignature", message: "Malformed Yandex signature." };
  }

  const sign = parts[0];
  const dataBase64 = parts[1];
  let payloadString;
  try {
    payloadString = Buffer.from(dataBase64, "base64").toString("utf8");
  } catch (error) {
    return { ok: false, error: "InvalidSignature", message: "Failed to decode Yandex signature payload." };
  }

  const expectedSign = crypto.createHmac("sha256", secretKey).update(payloadString).digest("base64");
  const signBuffer = Buffer.from(sign);
  const expectedBuffer = Buffer.from(expectedSign);
  if (signBuffer.length !== expectedBuffer.length || !crypto.timingSafeEqual(signBuffer, expectedBuffer)) {
    return { ok: false, error: "InvalidSignature", message: "Yandex signature verification failed." };
  }

  let payload;
  try {
    payload = JSON.parse(payloadString);
  } catch (error) {
    return { ok: false, error: "InvalidSignature", message: "Invalid Yandex signature JSON payload." };
  }

  const uniqueId = extractUniqueId(payload);
  if (!uniqueId) {
    return {
      ok: false,
      error: "InvalidSignature",
      message: "Yandex signature payload does not contain unique player id.",
    };
  }

  return {
    ok: true,
    uniqueId,
    payload,
    issuedAt: Number(payload && payload.issuedAt ? payload.issuedAt : 0),
  };
}

function verifyProfileAccess(externalPlayerId, req, body) {
  if (!isEnforced() || !isYandexExternalPlayerId(externalPlayerId)) {
    return { ok: true };
  }

  const verified = verifyYandexSignature(extractSignature(req, body), getSecretKey());
  if (!verified.ok) {
    return verified;
  }

  const expectedExternalId = buildExpectedExternalId(verified.uniqueId);
  const requestedExternalId = String(externalPlayerId || "").trim();
  if (requestedExternalId.toLowerCase() !== expectedExternalId.toLowerCase()) {
    return {
      ok: false,
      error: "PlayerIdMismatch",
      message: "Yandex signature does not match requested player id.",
    };
  }

  const maxAgeSec = Number(process.env.YANDEX_GAMES_SIGNATURE_MAX_AGE_SEC || 86400);
  if (verified.issuedAt > 0 && Number.isFinite(maxAgeSec) && maxAgeSec > 0) {
    const ageSec = Math.floor(Date.now() / 1000) - verified.issuedAt;
    if (ageSec > maxAgeSec) {
      return {
        ok: false,
        error: "SignatureExpired",
        message: "Yandex player signature expired.",
      };
    }
  }

  return { ok: true, uniqueId: verified.uniqueId };
}

module.exports = {
  buildExpectedExternalId,
  extractSignature,
  extractUniqueId,
  isEnforced,
  isYandexExternalPlayerId,
  verifyProfileAccess,
  verifyYandexSignature,
};
