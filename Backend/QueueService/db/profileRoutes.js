const playerRepository = require("./playerRepository");

function registerProfileRoutes({ readJsonBody, respondJson, getRequestUrl }) {
  return async function handleProfileRoutes(req, res, path, method) {
    if (method === "GET" && path === "/profile/nickname/available") {
      const requestUrl = getRequestUrl ? getRequestUrl(req) : null;
      const nickname = requestUrl ? requestUrl.searchParams.get("nickname") : "";
      const playerId = requestUrl ? requestUrl.searchParams.get("playerId") : "";
      const result = await playerRepository.isNicknameAvailable(nickname, playerId);
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

      try {
        const profile = await playerRepository.ensurePlayer(playerId);
        respondJson(res, 200, { ok: true, profile });
      } catch (error) {
        console.error("[profile][ensure] failed:", error && error.message ? error.message : error);
        respondJson(res, 500, {
          ok: false,
          error: "EnsureFailed",
          message: error && error.message ? error.message : "Failed to ensure player profile.",
        });
      }
      return true;
    }

    if (method === "GET" && path === "/profile/leaderboard") {
      const requestUrl = getRequestUrl ? getRequestUrl(req) : null;
      const limitRaw = requestUrl ? requestUrl.searchParams.get("limit") : "25";
      const mode = requestUrl ? requestUrl.searchParams.get("mode") : "duel";
      const entries = await playerRepository.getLeaderboard(limitRaw, mode);
      respondJson(res, 200, { ok: true, entries });
      return true;
    }

    if (method === "GET" && path.startsWith("/profile/")) {
      const externalPlayerId = decodeURIComponent(path.slice("/profile/".length));
      if (!externalPlayerId || externalPlayerId.includes("/") || externalPlayerId.startsWith("nickname")) {
        respondJson(res, 400, { ok: false, error: "InvalidPlayerId" });
        return true;
      }

      const profile = await playerRepository.getProfile(externalPlayerId);
      if (!profile) {
        respondJson(res, 404, { ok: false, error: "PlayerNotFound" });
        return true;
      }

      respondJson(res, 200, { ok: true, profile });
      return true;
    }

    if (method === "POST" && path.endsWith("/open-case")) {
      const prefix = "/profile/";
      const suffix = "/open-case";
      if (!path.startsWith(prefix) || !path.endsWith(suffix)) {
        return false;
      }

      const externalPlayerId = decodeURIComponent(
        path.slice(prefix.length, path.length - suffix.length)
      );
      const body = await readJsonBody(req);
      const caseId = body && body.caseId;
      const result = await playerRepository.openCase(externalPlayerId, caseId);
      respondJson(res, result.ok ? 200 : 400, result);
      return true;
    }

    if (method === "POST" && path.endsWith("/achievement-event")) {
      const prefix = "/profile/";
      const suffix = "/achievement-event";
      if (!path.startsWith(prefix) || !path.endsWith(suffix)) {
        return false;
      }

      const externalPlayerId = decodeURIComponent(
        path.slice(prefix.length, path.length - suffix.length)
      );
      const body = await readJsonBody(req);
      const eventType = body && body.eventType;
      const amount = body && body.amount;
      const result = await playerRepository.reportAchievementEvent(externalPlayerId, eventType, amount);
      respondJson(res, result.ok ? 200 : 400, result);
      return true;
    }

    if (method === "POST" && path.endsWith("/claim-achievement")) {
      const prefix = "/profile/";
      const suffix = "/claim-achievement";
      if (!path.startsWith(prefix) || !path.endsWith(suffix)) {
        return false;
      }

      const externalPlayerId = decodeURIComponent(
        path.slice(prefix.length, path.length - suffix.length)
      );
      const body = await readJsonBody(req);
      const achievementId = body && body.achievementId;
      const result = await playerRepository.claimAchievement(externalPlayerId, achievementId);
      respondJson(res, result.ok ? 200 : 400, result);
      return true;
    }

    if (method === "POST" && path.endsWith("/purchase-case")) {
      const prefix = "/profile/";
      const suffix = "/purchase-case";
      if (!path.startsWith(prefix) || !path.endsWith(suffix)) {
        return false;
      }

      const externalPlayerId = decodeURIComponent(
        path.slice(prefix.length, path.length - suffix.length)
      );
      const body = await readJsonBody(req);
      const caseId = body && body.caseId;
      const result = await playerRepository.purchaseCase(externalPlayerId, caseId);
      respondJson(res, result.ok ? 200 : 400, result);
      return true;
    }

    if (method === "POST" && path.endsWith("/grant-iap")) {
      const prefix = "/profile/";
      const suffix = "/grant-iap";
      if (!path.startsWith(prefix) || !path.endsWith(suffix)) {
        return false;
      }

      const externalPlayerId = decodeURIComponent(
        path.slice(prefix.length, path.length - suffix.length)
      );
      const body = await readJsonBody(req);
      const productId = body && body.productId;
      const result = await playerRepository.grantIapProduct(externalPlayerId, productId);
      respondJson(res, result.ok ? 200 : 400, result);
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
      const result = await playerRepository.purchaseSkin(externalPlayerId, skinId);
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
      const result = await playerRepository.setEquippedSlot(externalPlayerId, slot, skinId);
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
      const result = await playerRepository.setNickname(externalPlayerId, body && body.nickname);
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
      const result = await playerRepository.setSelectedCharacterModel(
        externalPlayerId,
        body && body.selectedCharacterModel
      );
      respondJson(res, result.ok ? 200 : 400, result);
      return true;
    }

    if (method === "POST" && path.endsWith("/match-stats")) {
      const prefix = "/profile/";
      const suffix = "/match-stats";
      if (!path.startsWith(prefix) || !path.endsWith(suffix)) {
        return false;
      }

      const externalPlayerId = decodeURIComponent(
        path.slice(prefix.length, path.length - suffix.length)
      );
      const body = await readJsonBody(req);
      const result = await playerRepository.recordMatchStats(externalPlayerId, body);
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
      const result = await playerRepository.grantMatchCurrency(externalPlayerId, amount, sourceId);
      respondJson(res, result.ok ? 200 : 400, result);
      return true;
    }

    return false;
  };
}

module.exports = {
  registerProfileRoutes,
};
