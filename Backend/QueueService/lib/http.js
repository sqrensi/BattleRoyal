"use strict";

function enableCors(res) {
  res.setHeader("Access-Control-Allow-Origin", "*");
  res.setHeader("Access-Control-Allow-Methods", "GET, POST, PUT, OPTIONS");
  res.setHeader("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Yandex-Player-Signature");
}

function respondJson(res, statusCode, payload) {
  const body = JSON.stringify(payload);
  res.writeHead(statusCode, {
    "Content-Type": "application/json; charset=utf-8",
    "Content-Length": Buffer.byteLength(body),
  });
  res.end(body);
}

class HttpRequestError extends Error {
  constructor(code, message) {
    super(message);
    this.name = "HttpRequestError";
    this.code = code;
  }
}

function readJsonBody(req) {
  return new Promise((resolve, reject) => {
    let raw = "";
    req.on("data", (chunk) => {
      raw += chunk;
      if (raw.length > 1024 * 1024) {
        reject(new HttpRequestError("BODY_TOO_LARGE", "Request body is too large."));
        req.destroy();
      }
    });
    req.on("end", () => {
      if (!raw) {
        resolve(null);
        return;
      }
      try {
        resolve(JSON.parse(raw));
      } catch (error) {
        reject(new HttpRequestError("INVALID_JSON_BODY", "Malformed JSON request body."));
      }
    });
    req.on("error", (error) => {
      reject(error);
    });
  });
}

function respondHttpRequestError(res, error) {
  if (!error || error.code === "INVALID_JSON_BODY") {
    respondJson(res, 400, {
      ok: false,
      error: "InvalidJson",
      message: "Malformed JSON request body.",
    });
    return;
  }

  if (error.code === "BODY_TOO_LARGE") {
    respondJson(res, 413, {
      ok: false,
      error: "BodyTooLarge",
      message: "Request body is too large.",
    });
    return;
  }

  respondJson(res, 500, {
    ok: false,
    error: "InternalError",
    message: "Unexpected server error.",
  });
}

function normalizePlayerId(value) {
  const id = String(value || "").trim();
  return id || `player-${Date.now()}`;
}

module.exports = {
  enableCors,
  respondJson,
  readJsonBody,
  respondHttpRequestError,
  normalizePlayerId,
};
