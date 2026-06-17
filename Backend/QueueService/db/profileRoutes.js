const playerRepository = require("./playerRepository");

function registerProfileRoutes({ readJsonBody, respondJson, getRequestUrl }) {
  return async function handleProfileRoutes(req, res, path, method) {
    if (method === "GET" && path === "/profile/nickname/available") {
      const requestUrl = getRequestUrl ? getRequestUrl(req) : null;
      const nickname = requestUrl ? requestUrl.searchParams.get("nickname") : "";
      const playerId = requestUrl ? requestUrl.searchParams.get("playerId") : "";
      const result = playerRepository.isNicknameAvailable(nickname, playerId);
      respondJson(res, 200, result);
      return true;
    }

    if (method === "POST" && path === "/profile/ensure") {
      const body = await readJsonBody(req);
      const playerId = body && body.playerId;
      if (!playerId) {
        respondJson(res, 400, { ok: false, error: "MissingPlayerId" });
        return true;
      }

      const profile = playerRepository.ensurePlayer(playerId);
      respondJson(res, 200, { ok: true, profile });
      return true;
    }

    if (method === "GET" && path.startsWith("/profile/")) {
      const externalPlayerId = decodeURIComponent(path.slice("/profile/".length));
      if (!externalPlayerId || externalPlayerId.includes("/") || externalPlayerId.startsWith("nickname")) {
        respondJson(res, 400, { ok: false, error: "InvalidPlayerId" });
        return true;
      }

      const profile = playerRepository.getProfile(externalPlayerId);
      if (!profile) {
        respondJson(res, 404, { ok: false, error: "PlayerNotFound" });
        return true;
      }

      respondJson(res, 200, { ok: true, profile });
      return true;
    }

    if (method === "POST" && path.endsWith("/purchase")) {
      const prefix = "/profile/";
      const suffix = "/purchase";
      if (!path.startsWith(prefix) || !path.endsWith(suffix)) {
        return false;
      }

      const externalPlayerId = decodeURIComponent(
        path.slice(prefix.length, path.length - suffix.length)
      );
      const body = await readJsonBody(req);
      const skinId = body && body.skinId;
      const result = playerRepository.purchaseSkin(externalPlayerId, skinId);
      respondJson(res, result.ok ? 200 : 400, result);
      return true;
    }

    if (method === "PUT" && path.endsWith("/equipped")) {
      const prefix = "/profile/";
      const suffix = "/equipped";
      if (!path.startsWith(prefix) || !path.endsWith(suffix)) {
        return false;
      }

      const externalPlayerId = decodeURIComponent(
        path.slice(prefix.length, path.length - suffix.length)
      );
      const body = await readJsonBody(req);
      const slot = body && body.slot;
      const skinId = body && body.skinId;
      const result = playerRepository.setEquippedSlot(externalPlayerId, slot, skinId);
      respondJson(res, result.ok ? 200 : 400, result);
      return true;
    }

    if (method === "PUT" && path.endsWith("/nickname")) {
      const prefix = "/profile/";
      const suffix = "/nickname";
      if (!path.startsWith(prefix) || !path.endsWith(suffix)) {
        return false;
      }

      const externalPlayerId = decodeURIComponent(
        path.slice(prefix.length, path.length - suffix.length)
      );
      const body = await readJsonBody(req);
      const result = playerRepository.setNickname(externalPlayerId, body && body.nickname);
      respondJson(res, result.ok ? 200 : 400, result);
      return true;
    }

    if (method === "PUT" && path.endsWith("/character-model")) {
      const prefix = "/profile/";
      const suffix = "/character-model";
      if (!path.startsWith(prefix) || !path.endsWith(suffix)) {
        return false;
      }

      const externalPlayerId = decodeURIComponent(
        path.slice(prefix.length, path.length - suffix.length)
      );
      const body = await readJsonBody(req);
      const result = playerRepository.setSelectedCharacterModel(
        externalPlayerId,
        body && body.selectedCharacterModel
      );
      respondJson(res, result.ok ? 200 : 400, result);
      return true;
    }

    if (method === "POST" && path.endsWith("/match-reward")) {
      const prefix = "/profile/";
      const suffix = "/match-reward";
      if (!path.startsWith(prefix) || !path.endsWith(suffix)) {
        return false;
      }

      const externalPlayerId = decodeURIComponent(
        path.slice(prefix.length, path.length - suffix.length)
      );
      const body = await readJsonBody(req);
      const amount = body && body.amount;
      const sourceId = body && body.sourceId;
      const result = playerRepository.grantMatchCurrency(externalPlayerId, amount, sourceId);
      respondJson(res, result.ok ? 200 : 400, result);
      return true;
    }

    return false;
  };
}

module.exports = {
  registerProfileRoutes,
};
