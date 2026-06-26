const {
  DEFAULT_OWNED_SKIN_IDS,
  DEFAULT_EQUIPPED,
  EQUIPMENT_SLOTS,
  isKnownSkinId,
  isDefaultOwnedSkinId,
  getShopPrice,
  slotForSkinId,
} = require("../data/shop-catalog");
const { getCaseDefinition, rollCaseLoot } = require("../data/case-catalog");
const {
  getAchievementDefinition,
  getAchievementsByEventType,
  getAllAchievements,
} = require("../data/achievement-catalog");
const crypto = require("crypto");
const { get, all, run, transaction, nowMs, newId, getDriverName } = require("./database");

function useDb(tx) {
  return tx || { get, all, run };
}

function buildRatingUpdateClause(ratingColumn) {
  const column = ratingColumn === "duel_rating" ? "duel_rating" : "rating";
  if (getDriverName() === "postgres") {
    return `SET ${column} = GREATEST(0, ${column} + ?)`;
  }

  return `SET ${column} = MAX(0, ${column} + ?)`;
}

const STARTER_CURRENCY = 100000;
const ITEM_TYPE_SKIN = "skin";
const ITEM_TYPE_CASE = "case";
const UNEQUIPPED_ATTACHMENT = "__none__";

function normalizeExternalPlayerId(value) {
  const trimmed = typeof value === "string" ? value.trim() : "";
  return trimmed || "anonymous";
}

function normalizeNickname(value) {
  if (typeof value !== "string") {
    return null;
  }

  const trimmed = value.trim();
  if (trimmed.length < 3 || trimmed.length > 16) {
    return null;
  }

  if (!/^[A-Za-z0-9_\u0410-\u042f\u0430-\u044f\u0401\u0451-]+$/.test(trimmed)) {
    return null;
  }

  return trimmed;
}

async function findPlayerIdByNickname(nickname, excludeInternalPlayerId) {
  if (!nickname) {
    return null;
  }

  const row = await get(
    `SELECT player_id
     FROM player_profiles
     WHERE nickname = ? COLLATE NOCASE
     LIMIT 1`,
    [nickname]
  );

  if (!row) {
    return null;
  }

  if (excludeInternalPlayerId && row.player_id === excludeInternalPlayerId) {
    return null;
  }

  return row.player_id;
}

async function generateUniqueNickname() {
  for (let attempt = 0; attempt < 64; attempt++) {
    const candidate = `Игрок_${1000 + Math.floor(Math.random() * 9000)}`;
    if (!(await findPlayerIdByNickname(candidate, null))) {
      return candidate;
    }
  }

  return `Игрок_${crypto.randomUUID().slice(0, 6)}`;
}

async function resolvePlayerNickname(externalPlayerId, ticketIdFallback) {
  const playerRow = await getPlayerByExternalId(externalPlayerId);
  if (playerRow && playerRow.nickname) {
    return playerRow.nickname;
  }

  if (typeof ticketIdFallback === "string" && ticketIdFallback.length >= 4) {
    return `Игрок_${ticketIdFallback.slice(0, 4)}`;
  }

  return "Игрок";
}

async function isNicknameAvailable(nickname, externalPlayerId) {
  const normalized = normalizeNickname(nickname);
  if (!normalized) {
    return {
      ok: false,
      available: false,
      error: "InvalidNickname",
      message: "Nickname must be 3-16 characters and use letters, numbers, _ or -.",
    };
  }

  const playerRow = externalPlayerId ? await getPlayerByExternalId(externalPlayerId) : null;
  const takenBy = await findPlayerIdByNickname(normalized, playerRow ? playerRow.id : null);
  if (takenBy) {
    return {
      ok: true,
      available: false,
      error: "NicknameTaken",
      message: "Nickname is already taken.",
      nickname: normalized,
    };
  }

  return {
    ok: true,
    available: true,
    nickname: normalized,
  };
}

async function getPlayerByExternalId(externalPlayerId) {
  return get(
    `SELECT p.id, p.external_player_id, p.created_at, p.updated_at,
            pp.nickname, pp.selected_character_model, pp.currency_balance,
            pp.starter_pack_granted, pp.rating, pp.duel_rating, pp.challenge_best_time_ms,
            pp.updated_at AS profile_updated_at
     FROM players p
     LEFT JOIN player_profiles pp ON pp.player_id = p.id
     WHERE p.external_player_id = ?`,
    [normalizeExternalPlayerId(externalPlayerId)]
  );
}

async function getOwnedSkinIds(playerId) {
  return (await getOwnedSkinQuantities(playerId)).map((entry) => entry.skinId);
}

async function getOwnedSkinQuantities(playerId) {
  const rows = await all(
    `SELECT item_id, quantity
     FROM player_owned_items
     WHERE player_id = ? AND item_type = ?
     ORDER BY item_id ASC`,
    [playerId, ITEM_TYPE_SKIN]
  );

  return rows.map((row) => ({
    skinId: row.item_id,
    quantity: Number.isFinite(row.quantity) && row.quantity > 0 ? row.quantity : 1,
  }));
}

async function grantOwnedSkin(playerId, skinId, source, allowDuplicateIncrement, tx = null) {
  const normalizedSkinId = String(skinId || "").trim();
  if (!normalizedSkinId || !isKnownSkinId(normalizedSkinId)) {
    return false;
  }

  const db = useDb(tx);
  const timestamp = nowMs();
  const existing = await db.get(
    `SELECT id, quantity
     FROM player_owned_items
     WHERE player_id = ? AND item_type = ? AND item_id = ?`,
    [playerId, ITEM_TYPE_SKIN, normalizedSkinId]
  );

  if (existing) {
    if (!allowDuplicateIncrement) {
      return false;
    }

    await db.run(
      `UPDATE player_owned_items
       SET quantity = quantity + 1,
           source = ?,
           acquired_at = ?
       WHERE id = ?`,
      [source, timestamp, existing.id]
    );
    return true;
  }

  await db.run(
    `INSERT INTO player_owned_items
     (player_id, item_type, item_id, source, acquired_at, quantity)
     VALUES (?, ?, ?, ?, ?, 1)`,
    [playerId, ITEM_TYPE_SKIN, normalizedSkinId, source, timestamp]
  );
  return true;
}

async function getOwnedCaseQuantities(playerId) {
  const rows = await all(
    `SELECT item_id, quantity
     FROM player_owned_items
     WHERE player_id = ? AND item_type = ?
     ORDER BY item_id ASC`,
    [playerId, ITEM_TYPE_CASE]
  );

  return rows.map((row) => ({
    caseId: row.item_id,
    quantity: Number.isFinite(row.quantity) && row.quantity > 0 ? row.quantity : 1,
  }));
}

async function grantOwnedCase(playerId, caseId, source, allowDuplicateIncrement, tx = null) {
  const normalizedCaseId = String(caseId || "").trim();
  if (!normalizedCaseId || !getCaseDefinition(normalizedCaseId)) {
    return false;
  }

  const db = useDb(tx);
  const timestamp = nowMs();
  const existing = await db.get(
    `SELECT id, quantity
     FROM player_owned_items
     WHERE player_id = ? AND item_type = ? AND item_id = ?`,
    [playerId, ITEM_TYPE_CASE, normalizedCaseId]
  );

  if (existing) {
    if (!allowDuplicateIncrement) {
      return false;
    }

    await db.run(
      `UPDATE player_owned_items
       SET quantity = quantity + 1,
           source = ?,
           acquired_at = ?
       WHERE id = ?`,
      [source, timestamp, existing.id]
    );
    return true;
  }

  await db.run(
    `INSERT INTO player_owned_items
     (player_id, item_type, item_id, source, acquired_at, quantity)
     VALUES (?, ?, ?, ?, ?, 1)`,
    [playerId, ITEM_TYPE_CASE, normalizedCaseId, source, timestamp]
  );
  return true;
}

async function consumeOwnedCase(playerId, caseId, tx = null) {
  const normalizedCaseId = String(caseId || "").trim();
  if (!normalizedCaseId) {
    return false;
  }

  const db = useDb(tx);
  const existing = await db.get(
    `SELECT id, quantity
     FROM player_owned_items
     WHERE player_id = ? AND item_type = ? AND item_id = ?`,
    [playerId, ITEM_TYPE_CASE, normalizedCaseId]
  );

  if (!existing || existing.quantity <= 0) {
    return false;
  }

  if (existing.quantity <= 1) {
    await db.run(`DELETE FROM player_owned_items WHERE id = ?`, [existing.id]);
  } else {
    await db.run(
      `UPDATE player_owned_items
       SET quantity = quantity - 1
       WHERE id = ?`,
      [existing.id]
    );
  }

  return true;
}

async function getEquippedMap(playerId) {
  const rows = await all(
    `SELECT slot_key, item_id
     FROM player_equipped_items
     WHERE player_id = ?`,
    [playerId]
  );

  const equipped = {};
  for (const slot of EQUIPMENT_SLOTS) {
    equipped[slot] = "";
  }

  for (const row of rows) {
    if (!row || !row.slot_key) {
      continue;
    }
    equipped[row.slot_key] = row.item_id || "";
  }

  return equipped;
}

function mapEquippedForClient(equippedMap) {
  return {
    shirt: equippedMap.shirt || "",
    pants: equippedMap.pants || "",
    boots: equippedMap.boots || "",
    gloves: equippedMap.gloves || "",
    face: equippedMap.face || "",
    hair: equippedMap.hair || "",
    weaponAssault: equippedMap.weapon_assault || "",
    weaponSniper: equippedMap.weapon_sniper || "",
    weaponPistol: equippedMap.weapon_pistol || "",
    weaponMp7: equippedMap.weapon_mp7 || "",
  };
}

async function ensureWeaponSkinDefaults(playerId) {
  const timestamp = nowMs();
  const weaponDefaults = [
    "weapon_ak47_000",
    "weapon_sniper_000",
    "weapon_pistol_000",
    "weapon_mp7_000",
  ];
  const weaponEquipped = {
    weapon_assault: "weapon_ak47_000",
    weapon_sniper: "weapon_sniper_000",
    weapon_pistol: "weapon_pistol_000",
    weapon_mp7: "weapon_mp7_000",
  };

  for (const skinId of weaponDefaults) {
    await run(
      `INSERT OR IGNORE INTO player_owned_items
       (player_id, item_type, item_id, source, acquired_at)
       VALUES (?, ?, ?, ?, ?)`,
      [playerId, ITEM_TYPE_SKIN, skinId, "weapon_defaults", timestamp]
    );
  }

  for (const [slot, skinId] of Object.entries(weaponEquipped)) {
    const existing = await get(
      `SELECT item_id
       FROM player_equipped_items
       WHERE player_id = ? AND slot_key = ?`,
      [playerId, slot]
    );
    if (!existing) {
      await run(
        `INSERT INTO player_equipped_items (player_id, slot_key, item_id, updated_at)
         VALUES (?, ?, ?, ?)`,
        [playerId, slot, skinId, timestamp]
      );
      continue;
    }

    if (!existing.item_id) {
      await run(
        `UPDATE player_equipped_items
         SET item_id = ?, updated_at = ?
         WHERE player_id = ? AND slot_key = ?`,
        [skinId, timestamp, playerId, slot]
      );
    }
  }
}

async function buildProfileResponse(playerRow) {
  if (!playerRow) {
    return null;
  }

  await ensureWeaponSkinDefaults(playerRow.id);

  const ownedSkins = await getOwnedSkinIds(playerRow.id);
  const equipped = mapEquippedForClient(await getEquippedMap(playerRow.id));

  return {
    playerId: playerRow.external_player_id,
    internalPlayerId: playerRow.id,
    nickname: playerRow.nickname || "",
    selectedCharacterModel: playerRow.selected_character_model || "",
    currencyBalance: Number.isFinite(playerRow.currency_balance)
      ? playerRow.currency_balance
      : 0,
    rating: Number.isFinite(playerRow.rating) ? Math.max(0, playerRow.rating) : 1000,
    duelRating: Number.isFinite(playerRow.duel_rating) ? Math.max(0, playerRow.duel_rating) : 1000,
    challengeBestTimeMs: Number.isFinite(playerRow.challenge_best_time_ms)
      ? Math.max(0, playerRow.challenge_best_time_ms)
      : -1,
    starterPackGranted: !!playerRow.starter_pack_granted,
    ownedSkins: ownedSkins,
    ownedSkinQuantities: await getOwnedSkinQuantities(playerRow.id),
    ownedCaseQuantities: await getOwnedCaseQuantities(playerRow.id),
    equipped,
    achievements: await listPlayerAchievements(playerRow.id),
    claimedRewards: await listPlayerRewardClaims(playerRow.id),
    stats: await getPlayerMatchStats(playerRow.id),
  };
}

async function ensurePlayerMatchStats(playerId) {
  if (!playerId) {
    return;
  }

  await run(
    `INSERT OR IGNORE INTO player_match_stats
     (player_id, match_count, total_kills, total_deaths, total_wins,
      total_placement_sum, total_damage, updated_at)
     VALUES (?, 0, 0, 0, 0, 0, 0, ?)`,
    [playerId, nowMs()]
  );
}

async function getPlayerMatchStats(playerId) {
  await ensurePlayerMatchStats(playerId);
  const row = await get(
    `SELECT match_count, total_kills, total_deaths, total_wins,
            total_placement_sum, total_damage
     FROM player_match_stats
     WHERE player_id = ?`,
    [playerId]
  );

  const matchCount = row && Number.isFinite(row.match_count) ? row.match_count : 0;
  const totalKills = row && Number.isFinite(row.total_kills) ? row.total_kills : 0;
  const totalDeaths = row && Number.isFinite(row.total_deaths) ? row.total_deaths : 0;
  const totalWins = row && Number.isFinite(row.total_wins) ? row.total_wins : 0;
  const totalPlacementSum =
    row && Number.isFinite(row.total_placement_sum) ? row.total_placement_sum : 0;
  const totalDamage = row && Number.isFinite(row.total_damage) ? row.total_damage : 0;

  const avgPlacement = matchCount > 0 ? totalPlacementSum / matchCount : 0;
  const avgDamage = matchCount > 0 ? totalDamage / matchCount : 0;
  const kdRatio = totalDeaths > 0 ? totalKills / totalDeaths : totalKills;

  return {
    matchCount,
    totalKills,
    totalDeaths,
    totalWins,
    avgPlacement,
    avgDamage,
    kdRatio,
  };
}

function calculateRatingDelta(placement, kills, matchMode, won) {
  const normalizedMode = String(matchMode || "").trim().toLowerCase();
  if (normalizedMode === "duel" || normalizedMode === "1v1") {
    if (won === true) {
      return 15;
    }
    if (won === false) {
      return -15;
    }
    return placement <= 1 ? 15 : -15;
  }

  const brSize = 20;
  const normalizedPlacement = Math.max(1, Math.min(brSize, Math.floor(Number(placement) || brSize)));
  const normalizedKills = Math.max(0, Math.floor(Number(kills) || 0));
  const placementDelta = Math.round(22 - ((normalizedPlacement - 1) / (brSize - 1)) * 44);
  const killBonus = Math.min(normalizedKills * 3, 15);
  return Math.max(-30, Math.min(30, placementDelta + killBonus));
}

async function recordMatchStats(externalPlayerId, payload) {
  const normalizedSourceId = String(payload && payload.sourceId ? payload.sourceId : "").trim();
  if (!normalizedSourceId) {
    return { ok: false, error: "MissingSourceId", message: "Match source id is required." };
  }

  const matchMode = String(payload && payload.matchMode ? payload.matchMode : "")
    .trim()
    .toLowerCase();
  const completionTimeMs = Math.max(0, Math.floor(Number(payload && payload.completionTimeMs)));

  if (matchMode === "challenge") {
    if (!Number.isFinite(completionTimeMs) || completionTimeMs <= 0) {
      return { ok: false, error: "InvalidStats", message: "Challenge completion time is required." };
    }

    const playerRow = await getPlayerByExternalId(externalPlayerId);
    if (!playerRow) {
      return { ok: false, error: "PlayerNotFound", message: "Player profile not found." };
    }

    const timestamp = nowMs();
    try {
      const alreadyReported = await transaction(async (tx) => {
        const existing = await tx.get(
          `SELECT id
           FROM player_match_stat_reports
           WHERE player_id = ? AND source_id = ?`,
          [playerRow.id, normalizedSourceId]
        );
        if (existing) {
          return true;
        }

        await tx.run(
          `UPDATE player_profiles
           SET challenge_best_time_ms = CASE
                 WHEN challenge_best_time_ms IS NULL OR challenge_best_time_ms <= 0 THEN ?
                 WHEN ? < challenge_best_time_ms THEN ?
                 ELSE challenge_best_time_ms
               END,
               updated_at = ?
           WHERE player_id = ?`,
          [completionTimeMs, completionTimeMs, completionTimeMs, timestamp, playerRow.id]
        );

        await tx.run(
          `INSERT INTO player_match_stat_reports
           (player_id, source_id, reported_at, rating_delta)
           VALUES (?, ?, ?, 0)`,
          [playerRow.id, normalizedSourceId, timestamp]
        );

        return false;
      });

      return {
        ok: true,
        alreadyReported,
        ratingDelta: 0,
        profile: await buildProfileResponse(await getPlayerByExternalId(externalPlayerId)),
      };
    } catch (error) {
      return {
        ok: false,
        error: "RecordFailed",
        message: error && error.message ? error.message : "Failed to record challenge time.",
      };
    }
  }

  const kills = Math.max(0, Math.floor(Number(payload && payload.kills)));
  const deaths = Math.max(0, Math.floor(Number(payload && payload.deaths)));
  const placement = Math.max(1, Math.floor(Number(payload && payload.placement)));
  const won = !!(payload && payload.won);
  const damageDealt = Math.max(0, Math.floor(Number(payload && payload.damageDealt)));

  if (!Number.isFinite(kills) || !Number.isFinite(deaths) || !Number.isFinite(placement)) {
    return { ok: false, error: "InvalidStats", message: "Match stats payload is invalid." };
  }

  const playerRow = await getPlayerByExternalId(externalPlayerId);
  if (!playerRow) {
    return { ok: false, error: "PlayerNotFound", message: "Player profile not found." };
  }

  await ensurePlayerMatchStats(playerRow.id);
  const timestamp = nowMs();

  try {
    let ratingDelta = 0;
    const alreadyReported = await transaction(async (tx) => {
      const existing = await tx.get(
        `SELECT id, rating_delta
         FROM player_match_stat_reports
         WHERE player_id = ? AND source_id = ?`,
        [playerRow.id, normalizedSourceId]
      );

      if (existing) {
        ratingDelta = Number.isFinite(existing.rating_delta) ? existing.rating_delta : 0;
        return true;
      }

      ratingDelta = calculateRatingDelta(placement, kills, payload && payload.matchMode, won);

      const ratingColumn = matchMode === "duel" || matchMode === "1v1" ? "duel_rating" : "rating";

      await tx.run(
        `UPDATE player_match_stats
         SET match_count = match_count + 1,
             total_kills = total_kills + ?,
             total_deaths = total_deaths + ?,
             total_wins = total_wins + ?,
             total_placement_sum = total_placement_sum + ?,
             total_damage = total_damage + ?,
             updated_at = ?
         WHERE player_id = ?`,
        [kills, deaths, won ? 1 : 0, placement, damageDealt, timestamp, playerRow.id]
      );

      await tx.run(
        `UPDATE player_profiles
         ${buildRatingUpdateClause(ratingColumn)},
             updated_at = ?
         WHERE player_id = ?`,
        [ratingDelta, timestamp, playerRow.id]
      );

      await tx.run(
        `INSERT INTO player_match_stat_reports
         (player_id, source_id, reported_at, rating_delta)
         VALUES (?, ?, ?, ?)`,
        [playerRow.id, normalizedSourceId, timestamp, ratingDelta]
      );

      return false;
    });

    return {
      ok: true,
      alreadyReported,
      ratingDelta,
      profile: await buildProfileResponse(await getPlayerByExternalId(externalPlayerId)),
    };
  } catch (error) {
    return {
      ok: false,
      error: "RecordFailed",
      message: error && error.message ? error.message : "Failed to record match stats.",
    };
  }
}

async function syncPlayerAchievements(playerId) {
  if (!playerId) {
    return;
  }

  const achievements = getAllAchievements();
  if (!Array.isArray(achievements) || achievements.length === 0) {
    return;
  }

  const timestamp = nowMs();
  for (let i = 0; i < achievements.length; i++) {
    const entry = achievements[i];
    if (!entry || !entry.achievementId) {
      continue;
    }

    await run(
      `INSERT OR IGNORE INTO player_achievements
       (player_id, achievement_id, progress, target, completed_at, updated_at)
       VALUES (?, ?, 0, ?, NULL, ?)`,
      [playerId, entry.achievementId, entry.target, timestamp]
    );
  }
}

async function assignDefaultNicknameIfMissing(internalPlayerId) {
  if (!internalPlayerId) {
    return;
  }

  const row = await get(`SELECT nickname FROM player_profiles WHERE player_id = ?`, [
    internalPlayerId,
  ]);
  if (!row || (typeof row.nickname === "string" && row.nickname.trim())) {
    return;
  }

  await run(
    `UPDATE player_profiles
     SET nickname = ?, updated_at = ?
     WHERE player_id = ?`,
    [await generateUniqueNickname(), nowMs(), internalPlayerId]
  );
}

async function ensurePlayer(externalPlayerId) {
  const normalizedExternalId = normalizeExternalPlayerId(externalPlayerId);
  let playerRow = await getPlayerByExternalId(normalizedExternalId);
  if (playerRow) {
    if (!playerRow.nickname) {
      await assignDefaultNicknameIfMissing(playerRow.id);
      playerRow = await getPlayerByExternalId(normalizedExternalId);
    }
    if (!playerRow.starter_pack_granted) {
      await grantStarterPack(playerRow.id);
      playerRow = await getPlayerByExternalId(normalizedExternalId);
    }
    await syncPlayerAchievements(playerRow.id);
    await ensurePlayerMatchStats(playerRow.id);
    return buildProfileResponse(playerRow);
  }

  const createdAt = nowMs();
  const playerId = newId();

  await transaction(async (tx) => {
    await tx.run(
      `INSERT INTO players (id, external_player_id, created_at, updated_at)
       VALUES (?, ?, ?, ?)`,
      [playerId, normalizedExternalId, createdAt, createdAt]
    );

    await tx.run(
      `INSERT INTO player_profiles (
         player_id, nickname, selected_character_model, currency_balance,
         starter_pack_granted, updated_at
       ) VALUES (?, ?, NULL, 0, FALSE, ?)`,
      [playerId, await generateUniqueNickname(), createdAt]
    );

    await grantStarterPack(playerId, tx);
  });

  playerRow = await getPlayerByExternalId(normalizedExternalId);
  await syncPlayerAchievements(playerRow.id);
  await ensurePlayerMatchStats(playerRow.id);
  return buildProfileResponse(playerRow);
}

async function grantStarterPack(playerId, outerTx = null) {
  const timestamp = nowMs();

  const work = async (tx) => {
    await tx.run(
      `UPDATE player_profiles
       SET currency_balance = currency_balance + ?,
           starter_pack_granted = TRUE,
           updated_at = ?
       WHERE player_id = ?`,
      [STARTER_CURRENCY, timestamp, playerId]
    );

    for (const skinId of DEFAULT_OWNED_SKIN_IDS) {
      await tx.run(
        `INSERT OR IGNORE INTO player_owned_items
         (player_id, item_type, item_id, source, acquired_at)
         VALUES (?, ?, ?, ?, ?)`,
        [playerId, ITEM_TYPE_SKIN, skinId, "starter", timestamp]
      );
    }

    for (const slot of EQUIPMENT_SLOTS) {
      await tx.run(
        `INSERT INTO player_equipped_items (player_id, slot_key, item_id, updated_at)
         VALUES (?, ?, ?, ?)
         ON CONFLICT(player_id, slot_key) DO UPDATE SET
           item_id = excluded.item_id,
           updated_at = excluded.updated_at`,
        [playerId, slot, DEFAULT_EQUIPPED[slot] || "", timestamp]
      );
    }
  };

  if (outerTx) {
    return work(outerTx);
  }

  return transaction(work);
}

async function purchaseSkin(externalPlayerId, skinId) {
  const normalizedSkinId = String(skinId || "").trim();
  if (!isKnownSkinId(normalizedSkinId)) {
    return { ok: false, error: "UnknownSkin", message: "Unknown skin id." };
  }
  if (isDefaultOwnedSkinId(normalizedSkinId)) {
    return { ok: false, error: "NotForSale", message: "Skin is not sold in shop." };
  }

  const playerRow = await getPlayerByExternalId(externalPlayerId);
  if (!playerRow) {
    return { ok: false, error: "PlayerNotFound", message: "Player profile not found." };
  }

  const owned = await getOwnedSkinIds(playerRow.id);
  if (owned.includes(normalizedSkinId)) {
    return { ok: false, error: "AlreadyOwned", message: "Skin already owned." };
  }

  const price = getShopPrice(normalizedSkinId);
  if (price <= 0) {
    return { ok: false, error: "NotForSale", message: "Skin is not sold in shop." };
  }

  const timestamp = nowMs();

  try {
    await transaction(async (tx) => {
      const profile = await tx.get(
        `SELECT currency_balance
         FROM player_profiles
         WHERE player_id = ?`,
        [playerRow.id]
      );

      if (!profile || profile.currency_balance < price) {
        throw new Error("INSUFFICIENT_FUNDS");
      }

      await tx.run(
        `UPDATE player_profiles
         SET currency_balance = currency_balance - ?,
             updated_at = ?
         WHERE player_id = ?`,
        [price, timestamp, playerRow.id]
      );

      await tx.run(
        `INSERT INTO player_owned_items
         (player_id, item_type, item_id, source, acquired_at)
         VALUES (?, ?, ?, ?, ?)`,
        [playerRow.id, ITEM_TYPE_SKIN, normalizedSkinId, "purchase", timestamp]
      );
    });
  } catch (error) {
    if (String(error.message || error) === "INSUFFICIENT_FUNDS") {
      return { ok: false, error: "InsufficientFunds", message: "Not enough currency." };
    }
    throw error;
  }

  return { ok: true, profile: await buildProfileResponse(await getPlayerByExternalId(externalPlayerId)) };
}

async function purchaseCase(externalPlayerId, caseId) {
  const normalizedCaseId = String(caseId || "").trim();
  const caseDefinition = getCaseDefinition(normalizedCaseId);
  if (!caseDefinition) {
    return { ok: false, error: "UnknownCase", message: "Unknown case id." };
  }

  if (!Array.isArray(caseDefinition.lootPool) || caseDefinition.lootPool.length === 0) {
    return { ok: false, error: "EmptyCase", message: "Case loot pool is empty." };
  }

  const price = caseDefinition.price;
  if (price <= 0) {
    return { ok: false, error: "NotForSale", message: "Case is not sold in shop." };
  }

  const playerRow = await getPlayerByExternalId(externalPlayerId);
  if (!playerRow) {
    return { ok: false, error: "PlayerNotFound", message: "Player profile not found." };
  }

  const timestamp = nowMs();

  try {
    await transaction(async (tx) => {
      const profile = await tx.get(
        `SELECT currency_balance
         FROM player_profiles
         WHERE player_id = ?`,
        [playerRow.id]
      );

      if (!profile || profile.currency_balance < price) {
        throw new Error("INSUFFICIENT_FUNDS");
      }

      await tx.run(
        `UPDATE player_profiles
         SET currency_balance = currency_balance - ?,
             updated_at = ?
         WHERE player_id = ?`,
        [price, timestamp, playerRow.id]
      );

      if (!(await grantOwnedCase(playerRow.id, normalizedCaseId, "purchase", true, tx))) {
        throw new Error("GRANT_FAILED");
      }
    });
  } catch (error) {
    if (String(error.message || error) === "INSUFFICIENT_FUNDS") {
      return { ok: false, error: "InsufficientFunds", message: "Not enough currency." };
    }

    if (String(error.message || error) === "GRANT_FAILED") {
      return { ok: false, error: "GrantFailed", message: "Failed to grant purchased case." };
    }

    throw error;
  }

  return {
    ok: true,
    profile: await buildProfileResponse(await getPlayerByExternalId(externalPlayerId)),
  };
}

async function openCase(externalPlayerId, caseId) {
  const normalizedCaseId = String(caseId || "").trim();
  const caseDefinition = getCaseDefinition(normalizedCaseId);
  if (!caseDefinition) {
    return { ok: false, error: "UnknownCase", message: "Unknown case id." };
  }

  if (!Array.isArray(caseDefinition.lootPool) || caseDefinition.lootPool.length === 0) {
    return { ok: false, error: "EmptyCase", message: "Case loot pool is empty." };
  }

  const playerRow = await getPlayerByExternalId(externalPlayerId);
  if (!playerRow) {
    return { ok: false, error: "PlayerNotFound", message: "Player profile not found." };
  }

  const rolledSkinId = rollCaseLoot(normalizedCaseId);
  if (!rolledSkinId || !isKnownSkinId(rolledSkinId)) {
    return { ok: false, error: "RollFailed", message: "Failed to roll case reward." };
  }

  const timestamp = nowMs();

  try {
    await transaction(async (tx) => {
      if (!(await consumeOwnedCase(playerRow.id, normalizedCaseId, tx))) {
        throw new Error("CASE_NOT_OWNED");
      }

      if (!(await grantOwnedSkin(playerRow.id, rolledSkinId, "case_open", true, tx))) {
        throw new Error("GRANT_FAILED");
      }

      await tx.run(
        `UPDATE player_profiles
         SET updated_at = ?
         WHERE player_id = ?`,
        [timestamp, playerRow.id]
      );
    });
  } catch (error) {
    if (String(error.message || error) === "CASE_NOT_OWNED") {
      return { ok: false, error: "CaseNotOwned", message: "Case is not in inventory." };
    }

    if (String(error.message || error) === "GRANT_FAILED") {
      return { ok: false, error: "GrantFailed", message: "Failed to grant rolled skin." };
    }

    throw error;
  }

  return {
    ok: true,
    rolledSkinId,
    profile: await buildProfileResponse(await getPlayerByExternalId(externalPlayerId)),
  };
}

async function setEquippedSlot(externalPlayerId, slotKey, itemId) {
  const normalizedSlot = String(slotKey || "").trim().toLowerCase();
  if (!EQUIPMENT_SLOTS.includes(normalizedSlot)) {
    return { ok: false, error: "InvalidSlot", message: "Unknown equipment slot." };
  }

  const playerRow = await getPlayerByExternalId(externalPlayerId);
  if (!playerRow) {
    return { ok: false, error: "PlayerNotFound", message: "Player profile not found." };
  }

  let normalizedItemId = String(itemId || "").trim();
  if (!normalizedItemId || normalizedItemId === UNEQUIPPED_ATTACHMENT) {
    if (normalizedSlot === "face" || normalizedSlot === "hair") {
      normalizedItemId = "";
    } else if (normalizedSlot.startsWith("weapon_")) {
      normalizedItemId = DEFAULT_EQUIPPED[normalizedSlot] || "";
      if (!normalizedItemId) {
        return { ok: false, error: "CannotUnequip", message: "Weapon slot cannot be reset." };
      }
    } else {
      return { ok: false, error: "CannotUnequip", message: "Clothing slot cannot be empty." };
    }
  }

  if (normalizedItemId) {
    if (!isKnownSkinId(normalizedItemId)) {
      return { ok: false, error: "UnknownSkin", message: "Unknown skin id." };
    }

    const expectedSlot = slotForSkinId(normalizedItemId);
    if (expectedSlot !== normalizedSlot) {
      return { ok: false, error: "WrongSlot", message: "Skin does not belong to this slot." };
    }

    const owned = await getOwnedSkinIds(playerRow.id);
    if (!owned.includes(normalizedItemId)) {
      return { ok: false, error: "NotOwned", message: "Skin is not owned." };
    }
  }

  await run(
    `INSERT INTO player_equipped_items (player_id, slot_key, item_id, updated_at)
     VALUES (?, ?, ?, ?)
     ON CONFLICT(player_id, slot_key) DO UPDATE SET
       item_id = excluded.item_id,
       updated_at = excluded.updated_at`,
    [playerRow.id, normalizedSlot, normalizedItemId, nowMs()]
  );

  return { ok: true, profile: await buildProfileResponse(await getPlayerByExternalId(externalPlayerId)) };
}

async function setNickname(externalPlayerId, nickname) {
  const normalizedNickname = normalizeNickname(nickname);
  if (!normalizedNickname) {
    return { ok: false, error: "InvalidNickname", message: "Nickname is empty or invalid." };
  }

  const playerRow = await getPlayerByExternalId(externalPlayerId);
  if (!playerRow) {
    return { ok: false, error: "PlayerNotFound", message: "Player profile not found." };
  }

  if (await findPlayerIdByNickname(normalizedNickname, playerRow.id)) {
    return { ok: false, error: "NicknameTaken", message: "Nickname is already taken." };
  }

  const timestamp = nowMs();
  try {
    await transaction(async (tx) => {
      await tx.run(
        `UPDATE player_profiles
         SET nickname = ?, updated_at = ?
         WHERE player_id = ?`,
        [normalizedNickname, timestamp, playerRow.id]
      );

      await tx.run(
        `INSERT INTO player_nickname_history (player_id, nickname, changed_at)
         VALUES (?, ?, ?)`,
        [playerRow.id, normalizedNickname, timestamp]
      );
    });
  } catch (error) {
    if (String(error.message || error).includes("UNIQUE")) {
      return { ok: false, error: "NicknameTaken", message: "Nickname is already taken." };
    }

    throw error;
  }

  return { ok: true, profile: await buildProfileResponse(await getPlayerByExternalId(externalPlayerId)) };
}

async function setSelectedCharacterModel(externalPlayerId, modelName) {
  const normalizedModel = typeof modelName === "string" ? modelName.trim().slice(0, 64) : "";
  const playerRow = await getPlayerByExternalId(externalPlayerId);
  if (!playerRow) {
    return { ok: false, error: "PlayerNotFound", message: "Player profile not found." };
  }

  await run(
    `UPDATE player_profiles
     SET selected_character_model = ?, updated_at = ?
     WHERE player_id = ?`,
    [normalizedModel, nowMs(), playerRow.id]
  );

  return { ok: true, profile: await buildProfileResponse(await getPlayerByExternalId(externalPlayerId)) };
}

async function listPlayerAchievements(playerId) {
  const rows = await all(
    `SELECT pa.achievement_id, pa.progress, pa.target, pa.completed_at, pa.claimed_at,
            ad.code, ad.title, ad.description, ad.category, ad.is_hidden
     FROM player_achievements pa
     JOIN achievement_definitions ad ON ad.id = pa.achievement_id
     WHERE pa.player_id = ? AND pa.claimed_at IS NULL
     ORDER BY ad.sort_order ASC, ad.title ASC`,
    [playerId]
  );

  return rows.map((row) => {
    const definition = getAchievementDefinition(row.achievement_id);
    const reward = definition ? definition.reward : null;
    return {
      achievementId: row.achievement_id,
      code: row.code,
      title: row.title,
      description: row.description || "",
      category: row.category || "",
      isHidden: !!row.is_hidden,
      progress: row.progress,
      target: row.target,
      completed: !!row.completed_at,
      completedAt: row.completed_at || 0,
      rewardType: reward ? reward.type : "",
      rewardAmount: reward
        ? reward.type === "currency"
          ? reward.amount
          : reward.amount
        : 0,
      rewardCaseId: reward && reward.type === "case" ? reward.caseId : "",
    };
  });
}

async function reportAchievementEvent(externalPlayerId, eventType, amount) {
  const normalizedEventType = String(eventType || "").trim();
  if (!normalizedEventType) {
    return { ok: false, error: "MissingEventType", message: "Event type is required." };
  }

  const increment = Math.max(1, Math.floor(Number(amount) || 1));
  const playerRow = await getPlayerByExternalId(externalPlayerId);
  if (!playerRow) {
    return { ok: false, error: "PlayerNotFound", message: "Player profile not found." };
  }

  await syncPlayerAchievements(playerRow.id);
  const matching = getAchievementsByEventType(normalizedEventType);
  if (!Array.isArray(matching) || matching.length === 0) {
    return {
      ok: true,
      newlyCompleted: [],
      profile: await buildProfileResponse(playerRow),
    };
  }

  const timestamp = nowMs();
  const newlyCompleted = [];

  await transaction(async (tx) => {
    for (let i = 0; i < matching.length; i++) {
      const definition = matching[i];
      const row = await tx.get(
        `SELECT progress, target, completed_at, claimed_at
         FROM player_achievements
         WHERE player_id = ? AND achievement_id = ?`,
        [playerRow.id, definition.achievementId]
      );

      if (!row || row.completed_at || row.claimed_at) {
        continue;
      }

      const target = Math.max(1, Number(row.target) || definition.target || 1);
      const nextProgress = Math.min(target, (Number(row.progress) || 0) + increment);
      const completedAt = nextProgress >= target ? timestamp : null;

      await tx.run(
        `UPDATE player_achievements
         SET progress = ?,
             target = ?,
             completed_at = COALESCE(completed_at, ?),
             updated_at = ?
         WHERE player_id = ? AND achievement_id = ?`,
        [nextProgress, target, completedAt, timestamp, playerRow.id, definition.achievementId]
      );

      if (completedAt && !row.completed_at) {
        newlyCompleted.push({
          achievementId: definition.achievementId,
          title: definition.title,
          description: definition.description || "",
        });
      }
    }
  });

  return {
    ok: true,
    newlyCompleted,
    profile: await buildProfileResponse(await getPlayerByExternalId(externalPlayerId)),
  };
}

async function claimAchievement(externalPlayerId, achievementId) {
  const normalizedAchievementId = String(achievementId || "").trim();
  const definition = getAchievementDefinition(normalizedAchievementId);
  if (!definition) {
    return { ok: false, error: "UnknownAchievement", message: "Unknown achievement id." };
  }

  const playerRow = await getPlayerByExternalId(externalPlayerId);
  if (!playerRow) {
    return { ok: false, error: "PlayerNotFound", message: "Player profile not found." };
  }

  const timestamp = nowMs();
  const row = await get(
    `SELECT completed_at, claimed_at
     FROM player_achievements
     WHERE player_id = ? AND achievement_id = ?`,
    [playerRow.id, normalizedAchievementId]
  );

  if (!row || !row.completed_at) {
    return { ok: false, error: "NotCompleted", message: "Achievement is not completed yet." };
  }

  if (row.claimed_at) {
    return { ok: false, error: "AlreadyClaimed", message: "Achievement reward already claimed." };
  }

  try {
    await transaction(async (tx) => {
      if (definition.reward.type === "currency") {
        await tx.run(
          `UPDATE player_profiles
           SET currency_balance = currency_balance + ?,
               updated_at = ?
           WHERE player_id = ?`,
          [definition.reward.amount, timestamp, playerRow.id]
        );
      } else if (definition.reward.type === "case") {
        for (let i = 0; i < definition.reward.amount; i++) {
          if (!(await grantOwnedCase(playerRow.id, definition.reward.caseId, "achievement", true, tx))) {
            throw new Error("GRANT_FAILED");
          }
        }
      } else {
        throw new Error("UNKNOWN_REWARD");
      }

      await tx.run(
        `UPDATE player_achievements
         SET claimed_at = ?,
             updated_at = ?
         WHERE player_id = ? AND achievement_id = ?`,
        [timestamp, timestamp, playerRow.id, normalizedAchievementId]
      );
    });
  } catch (error) {
    if (String(error.message || error) === "GRANT_FAILED") {
      return { ok: false, error: "GrantFailed", message: "Failed to grant achievement reward." };
    }

    if (String(error.message || error) === "UNKNOWN_REWARD") {
      return { ok: false, error: "UnknownReward", message: "Unsupported achievement reward." };
    }

    throw error;
  }

  return {
    ok: true,
    profile: await buildProfileResponse(await getPlayerByExternalId(externalPlayerId)),
  };
}

async function listPlayerRewardClaims(playerId) {
  const rows = await all(
    `SELECT prc.reward_id, prc.source_type, prc.source_id, prc.claimed_at,
            rd.code, rd.reward_type, rd.payload_json
     FROM player_reward_claims prc
     JOIN reward_definitions rd ON rd.id = prc.reward_id
     WHERE prc.player_id = ?
     ORDER BY prc.claimed_at DESC`,
    [playerId]
  );

  return rows.map((row) => ({
    rewardId: row.reward_id,
    code: row.code,
    rewardType: row.reward_type,
    payloadJson: row.payload_json,
    sourceType: row.source_type,
    sourceId: row.source_id || "",
    claimedAt: row.claimed_at,
  }));
}

async function playerOwnsSkin(externalPlayerId, skinId) {
  const playerRow = await getPlayerByExternalId(externalPlayerId);
  if (!playerRow) {
    return false;
  }
  const normalizedSkinId = String(skinId || "").trim();
  if (!normalizedSkinId) {
    return false;
  }
  const owned = await getOwnedSkinIds(playerRow.id);
  return owned.includes(normalizedSkinId);
}

async function grantMatchCurrency(externalPlayerId, amount, sourceId) {
  const normalizedAmount = Math.floor(Number(amount));
  if (!Number.isFinite(normalizedAmount) || normalizedAmount <= 0) {
    return { ok: false, error: "InvalidAmount", message: "Reward amount must be positive." };
  }

  const normalizedSourceId = String(sourceId || "").trim();
  if (!normalizedSourceId) {
    return { ok: false, error: "MissingSourceId", message: "Match source id is required." };
  }

  const playerRow = await getPlayerByExternalId(externalPlayerId);
  if (!playerRow) {
    return { ok: false, error: "PlayerNotFound", message: "Player profile not found." };
  }

  const timestamp = nowMs();
  const grantType = "match_reward";

  try {
    const alreadyGranted = await transaction(async (tx) => {
      const existing = await tx.get(
        `SELECT id
         FROM player_currency_grants
         WHERE player_id = ? AND grant_type = ? AND source_id = ?`,
        [playerRow.id, grantType, normalizedSourceId]
      );

      if (existing) {
        return true;
      }

      await tx.run(
        `UPDATE player_profiles
         SET currency_balance = currency_balance + ?,
             updated_at = ?
         WHERE player_id = ?`,
        [normalizedAmount, timestamp, playerRow.id]
      );

      await tx.run(
        `INSERT INTO player_currency_grants
         (player_id, grant_type, source_id, amount, granted_at)
         VALUES (?, ?, ?, ?, ?)`,
        [playerRow.id, grantType, normalizedSourceId, normalizedAmount, timestamp]
      );

      return false;
    });

    return {
      ok: true,
      alreadyGranted,
      profile: await buildProfileResponse(await getPlayerByExternalId(externalPlayerId)),
    };
  } catch (error) {
    return {
      ok: false,
      error: "GrantFailed",
      message: error && error.message ? error.message : "Failed to grant match reward.",
    };
  }
}

async function getLeaderboard(limit = 25, mode = "duel") {
  const normalizedLimit = Math.max(1, Math.min(25, Math.floor(Number(limit) || 25)));
  const normalizedMode = String(mode || "duel").trim().toLowerCase();

  if (normalizedMode === "challenge") {
    const rows = await all(
      `SELECT COALESCE(NULLIF(TRIM(pp.nickname), ''), 'Игрок') AS nickname,
              pp.challenge_best_time_ms AS challenge_time_ms,
              p.external_player_id AS player_id
       FROM player_profiles pp
       INNER JOIN players p ON p.id = pp.player_id
       WHERE pp.challenge_best_time_ms IS NOT NULL AND pp.challenge_best_time_ms > 0
       ORDER BY pp.challenge_best_time_ms ASC, pp.updated_at ASC
       LIMIT ?`,
      [normalizedLimit]
    );

    return rows.map((row, index) => ({
      rank: index + 1,
      nickname: row.nickname || "Игрок",
      rating: 0,
      challengeTimeMs: Number.isFinite(row.challenge_time_ms) ? row.challenge_time_ms : -1,
      playerId: row.player_id || "",
    }));
  }

  const ratingColumn = normalizedMode === "duel" || normalizedMode === "1v1" ? "duel_rating" : "rating";
  const rows = await all(
    `SELECT COALESCE(NULLIF(TRIM(pp.nickname), ''), 'Игрок') AS nickname,
            pp.${ratingColumn} AS rating,
            p.external_player_id AS player_id
     FROM player_profiles pp
     INNER JOIN players p ON p.id = pp.player_id
     ORDER BY pp.${ratingColumn} DESC, pp.updated_at ASC
     LIMIT ?`,
    [normalizedLimit]
  );

  return rows.map((row, index) => ({
    rank: index + 1,
    nickname: row.nickname || "Игрок",
    rating: Number.isFinite(row.rating) ? Math.max(0, row.rating) : 1000,
    challengeTimeMs: -1,
    playerId: row.player_id || "",
  }));
}

module.exports = {
  ensurePlayer,
  getProfile: async (externalPlayerId) => {
    const playerRow = await getPlayerByExternalId(externalPlayerId);
    if (!playerRow) {
      return null;
    }
    if (!playerRow.starter_pack_granted) {
      await grantStarterPack(playerRow.id);
    }
    await syncPlayerAchievements(playerRow.id);
    await ensurePlayerMatchStats(playerRow.id);
    return buildProfileResponse(await getPlayerByExternalId(externalPlayerId));
  },
  purchaseSkin,
  purchaseCase,
  openCase,
  reportAchievementEvent,
  claimAchievement,
  setEquippedSlot,
  setNickname,
  setSelectedCharacterModel,
  grantMatchCurrency,
  recordMatchStats,
  getLeaderboard,
  playerOwnsSkin,
  isNicknameAvailable,
  resolvePlayerNickname,
};
