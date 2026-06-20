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
const { openDatabase, nowMs, newId } = require("./database");

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

function findPlayerIdByNickname(nickname, excludeInternalPlayerId) {
  if (!nickname) {
    return null;
  }

  const row = openDatabase()
    .prepare(
      `SELECT player_id
       FROM player_profiles
       WHERE nickname = ? COLLATE NOCASE
       LIMIT 1`
    )
    .get(nickname);

  if (!row) {
    return null;
  }

  if (excludeInternalPlayerId && row.player_id === excludeInternalPlayerId) {
    return null;
  }

  return row.player_id;
}

function generateUniqueNickname() {
  for (let attempt = 0; attempt < 64; attempt++) {
    const candidate = `Игрок_${1000 + Math.floor(Math.random() * 9000)}`;
    if (!findPlayerIdByNickname(candidate, null)) {
      return candidate;
    }
  }

  return `Игрок_${crypto.randomUUID().slice(0, 6)}`;
}

function resolvePlayerNickname(externalPlayerId, ticketIdFallback) {
  const playerRow = getPlayerByExternalId(externalPlayerId);
  if (playerRow && playerRow.nickname) {
    return playerRow.nickname;
  }

  if (typeof ticketIdFallback === "string" && ticketIdFallback.length >= 4) {
    return `Игрок_${ticketIdFallback.slice(0, 4)}`;
  }

  return "Игрок";
}

function isNicknameAvailable(nickname, externalPlayerId) {
  const normalized = normalizeNickname(nickname);
  if (!normalized) {
    return {
      ok: false,
      available: false,
      error: "InvalidNickname",
      message: "Nickname must be 3-16 characters and use letters, numbers, _ or -.",
    };
  }

  const playerRow = externalPlayerId ? getPlayerByExternalId(externalPlayerId) : null;
  const takenBy = findPlayerIdByNickname(normalized, playerRow ? playerRow.id : null);
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

function getPlayerByExternalId(externalPlayerId) {
  const db = openDatabase();
  return db
    .prepare(
      `SELECT p.id, p.external_player_id, p.created_at, p.updated_at,
              pp.nickname, pp.selected_character_model, pp.currency_balance,
              pp.starter_pack_granted, pp.rating, pp.updated_at AS profile_updated_at
       FROM players p
       LEFT JOIN player_profiles pp ON pp.player_id = p.id
       WHERE p.external_player_id = ?`
    )
    .get(normalizeExternalPlayerId(externalPlayerId));
}

function getOwnedSkinIds(playerId) {
  return getOwnedSkinQuantities(playerId).map((entry) => entry.skinId);
}

function getOwnedSkinQuantities(playerId) {
  const db = openDatabase();
  const rows = db
    .prepare(
      `SELECT item_id, quantity
       FROM player_owned_items
       WHERE player_id = ? AND item_type = ?
       ORDER BY item_id ASC`
    )
    .all(playerId, ITEM_TYPE_SKIN);

  return rows.map((row) => ({
    skinId: row.item_id,
    quantity: Number.isFinite(row.quantity) && row.quantity > 0 ? row.quantity : 1,
  }));
}

function grantOwnedSkin(playerId, skinId, source, allowDuplicateIncrement) {
  const normalizedSkinId = String(skinId || "").trim();
  if (!normalizedSkinId || !isKnownSkinId(normalizedSkinId)) {
    return false;
  }

  const db = openDatabase();
  const timestamp = nowMs();
  const existing = db
    .prepare(
      `SELECT id, quantity
       FROM player_owned_items
       WHERE player_id = ? AND item_type = ? AND item_id = ?`
    )
    .get(playerId, ITEM_TYPE_SKIN, normalizedSkinId);

  if (existing) {
    if (!allowDuplicateIncrement) {
      return false;
    }

    db.prepare(
      `UPDATE player_owned_items
       SET quantity = quantity + 1,
           source = ?,
           acquired_at = ?
       WHERE id = ?`
    ).run(source, timestamp, existing.id);
    return true;
  }

  db.prepare(
    `INSERT INTO player_owned_items
     (player_id, item_type, item_id, source, acquired_at, quantity)
     VALUES (?, ?, ?, ?, ?, 1)`
  ).run(playerId, ITEM_TYPE_SKIN, normalizedSkinId, source, timestamp);
  return true;
}

function getOwnedCaseQuantities(playerId) {
  const db = openDatabase();
  const rows = db
    .prepare(
      `SELECT item_id, quantity
       FROM player_owned_items
       WHERE player_id = ? AND item_type = ?
       ORDER BY item_id ASC`
    )
    .all(playerId, ITEM_TYPE_CASE);

  return rows.map((row) => ({
    caseId: row.item_id,
    quantity: Number.isFinite(row.quantity) && row.quantity > 0 ? row.quantity : 1,
  }));
}

function grantOwnedCase(playerId, caseId, source, allowDuplicateIncrement) {
  const normalizedCaseId = String(caseId || "").trim();
  if (!normalizedCaseId || !getCaseDefinition(normalizedCaseId)) {
    return false;
  }

  const db = openDatabase();
  const timestamp = nowMs();
  const existing = db
    .prepare(
      `SELECT id, quantity
       FROM player_owned_items
       WHERE player_id = ? AND item_type = ? AND item_id = ?`
    )
    .get(playerId, ITEM_TYPE_CASE, normalizedCaseId);

  if (existing) {
    if (!allowDuplicateIncrement) {
      return false;
    }

    db.prepare(
      `UPDATE player_owned_items
       SET quantity = quantity + 1,
           source = ?,
           acquired_at = ?
       WHERE id = ?`
    ).run(source, timestamp, existing.id);
    return true;
  }

  db.prepare(
    `INSERT INTO player_owned_items
     (player_id, item_type, item_id, source, acquired_at, quantity)
     VALUES (?, ?, ?, ?, ?, 1)`
  ).run(playerId, ITEM_TYPE_CASE, normalizedCaseId, source, timestamp);
  return true;
}

function consumeOwnedCase(playerId, caseId) {
  const normalizedCaseId = String(caseId || "").trim();
  if (!normalizedCaseId) {
    return false;
  }

  const db = openDatabase();
  const existing = db
    .prepare(
      `SELECT id, quantity
       FROM player_owned_items
       WHERE player_id = ? AND item_type = ? AND item_id = ?`
    )
    .get(playerId, ITEM_TYPE_CASE, normalizedCaseId);

  if (!existing || existing.quantity <= 0) {
    return false;
  }

  if (existing.quantity <= 1) {
    db.prepare(`DELETE FROM player_owned_items WHERE id = ?`).run(existing.id);
  } else {
    db.prepare(
      `UPDATE player_owned_items
       SET quantity = quantity - 1
       WHERE id = ?`
    ).run(existing.id);
  }

  return true;
}

function getEquippedMap(playerId) {
  const db = openDatabase();
  const rows = db
    .prepare(
      `SELECT slot_key, item_id
       FROM player_equipped_items
       WHERE player_id = ?`
    )
    .all(playerId);

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

function ensureWeaponSkinDefaults(playerId) {
  const db = openDatabase();
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

  const insertOwned = db.prepare(
    `INSERT OR IGNORE INTO player_owned_items
     (player_id, item_type, item_id, source, acquired_at)
     VALUES (?, ?, ?, ?, ?)`
  );
  for (const skinId of weaponDefaults) {
    insertOwned.run(playerId, ITEM_TYPE_SKIN, skinId, "weapon_defaults", timestamp);
  }

  const selectEquipped = db.prepare(
    `SELECT item_id
     FROM player_equipped_items
     WHERE player_id = ? AND slot_key = ?`
  );
  const insertEquipped = db.prepare(
    `INSERT INTO player_equipped_items (player_id, slot_key, item_id, updated_at)
     VALUES (?, ?, ?, ?)`
  );
  const updateEquipped = db.prepare(
    `UPDATE player_equipped_items
     SET item_id = ?, updated_at = ?
     WHERE player_id = ? AND slot_key = ?`
  );

  for (const [slot, skinId] of Object.entries(weaponEquipped)) {
    const existing = selectEquipped.get(playerId, slot);
    if (!existing) {
      insertEquipped.run(playerId, slot, skinId, timestamp);
      continue;
    }

    if (!existing.item_id) {
      updateEquipped.run(skinId, timestamp, playerId, slot);
    }
  }
}

function buildProfileResponse(playerRow) {
  if (!playerRow) {
    return null;
  }

  ensureWeaponSkinDefaults(playerRow.id);

  const ownedSkins = getOwnedSkinIds(playerRow.id);
  const equipped = mapEquippedForClient(getEquippedMap(playerRow.id));

  return {
    playerId: playerRow.external_player_id,
    internalPlayerId: playerRow.id,
    nickname: playerRow.nickname || "",
    selectedCharacterModel: playerRow.selected_character_model || "",
    currencyBalance: Number.isFinite(playerRow.currency_balance)
      ? playerRow.currency_balance
      : 0,
    rating: Number.isFinite(playerRow.rating) ? Math.max(0, playerRow.rating) : 1000,
    starterPackGranted: !!playerRow.starter_pack_granted,
    ownedSkins: ownedSkins,
    ownedSkinQuantities: getOwnedSkinQuantities(playerRow.id),
    ownedCaseQuantities: getOwnedCaseQuantities(playerRow.id),
    equipped,
    achievements: listPlayerAchievements(playerRow.id),
    claimedRewards: listPlayerRewardClaims(playerRow.id),
    stats: getPlayerMatchStats(playerRow.id),
  };
}

function ensurePlayerMatchStats(playerId) {
  if (!playerId) {
    return;
  }

  const db = openDatabase();
  db.prepare(
    `INSERT OR IGNORE INTO player_match_stats
     (player_id, match_count, total_kills, total_deaths, total_wins,
      total_placement_sum, total_damage, updated_at)
     VALUES (?, 0, 0, 0, 0, 0, 0, ?)`
  ).run(playerId, nowMs());
}

function getPlayerMatchStats(playerId) {
  ensurePlayerMatchStats(playerId);
  const db = openDatabase();
  const row = db
    .prepare(
      `SELECT match_count, total_kills, total_deaths, total_wins,
              total_placement_sum, total_damage
       FROM player_match_stats
       WHERE player_id = ?`
    )
    .get(playerId);

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

function calculateRatingDelta(placement, kills) {
  const brSize = 20;
  const normalizedPlacement = Math.max(1, Math.min(brSize, Math.floor(Number(placement) || brSize)));
  const normalizedKills = Math.max(0, Math.floor(Number(kills) || 0));
  const placementDelta = Math.round(22 - ((normalizedPlacement - 1) / (brSize - 1)) * 44);
  const killBonus = Math.min(normalizedKills * 3, 15);
  return Math.max(-30, Math.min(30, placementDelta + killBonus));
}

function recordMatchStats(externalPlayerId, payload) {
  const normalizedSourceId = String(payload && payload.sourceId ? payload.sourceId : "").trim();
  if (!normalizedSourceId) {
    return { ok: false, error: "MissingSourceId", message: "Match source id is required." };
  }

  const kills = Math.max(0, Math.floor(Number(payload && payload.kills)));
  const deaths = Math.max(0, Math.floor(Number(payload && payload.deaths)));
  const placement = Math.max(1, Math.floor(Number(payload && payload.placement)));
  const won = !!(payload && payload.won);
  const damageDealt = Math.max(0, Math.floor(Number(payload && payload.damageDealt)));

  if (!Number.isFinite(kills) || !Number.isFinite(deaths) || !Number.isFinite(placement)) {
    return { ok: false, error: "InvalidStats", message: "Match stats payload is invalid." };
  }

  const playerRow = getPlayerByExternalId(externalPlayerId);
  if (!playerRow) {
    return { ok: false, error: "PlayerNotFound", message: "Player profile not found." };
  }

  ensurePlayerMatchStats(playerRow.id);
  const db = openDatabase();
  const timestamp = nowMs();

  try {
    let ratingDelta = 0;
    const alreadyReported = db.transaction(() => {
      const existing = db
        .prepare(
          `SELECT id, rating_delta
           FROM player_match_stat_reports
           WHERE player_id = ? AND source_id = ?`
        )
        .get(playerRow.id, normalizedSourceId);

      if (existing) {
        ratingDelta = Number.isFinite(existing.rating_delta) ? existing.rating_delta : 0;
        return true;
      }

      ratingDelta = calculateRatingDelta(placement, kills);

      db.prepare(
        `UPDATE player_match_stats
         SET match_count = match_count + 1,
             total_kills = total_kills + ?,
             total_deaths = total_deaths + ?,
             total_wins = total_wins + ?,
             total_placement_sum = total_placement_sum + ?,
             total_damage = total_damage + ?,
             updated_at = ?
         WHERE player_id = ?`
      ).run(kills, deaths, won ? 1 : 0, placement, damageDealt, timestamp, playerRow.id);

      db.prepare(
        `UPDATE player_profiles
         SET rating = MAX(0, rating + ?),
             updated_at = ?
         WHERE player_id = ?`
      ).run(ratingDelta, timestamp, playerRow.id);

      db.prepare(
        `INSERT INTO player_match_stat_reports
         (player_id, source_id, reported_at, rating_delta)
         VALUES (?, ?, ?, ?)`
      ).run(playerRow.id, normalizedSourceId, timestamp, ratingDelta);

      return false;
    })();

    return {
      ok: true,
      alreadyReported,
      ratingDelta,
      profile: buildProfileResponse(getPlayerByExternalId(externalPlayerId)),
    };
  } catch (error) {
    return {
      ok: false,
      error: "RecordFailed",
      message: error && error.message ? error.message : "Failed to record match stats.",
    };
  }
}

function syncPlayerAchievements(playerId) {
  if (!playerId) {
    return;
  }

  const db = openDatabase();
  const achievements = getAllAchievements();
  if (!Array.isArray(achievements) || achievements.length === 0) {
    return;
  }

  const timestamp = nowMs();
  const insert = db.prepare(
    `INSERT OR IGNORE INTO player_achievements
     (player_id, achievement_id, progress, target, completed_at, updated_at)
     VALUES (?, ?, 0, ?, NULL, ?)`
  );

  for (let i = 0; i < achievements.length; i++) {
    const entry = achievements[i];
    if (!entry || !entry.achievementId) {
      continue;
    }

    insert.run(playerId, entry.achievementId, entry.target, timestamp);
  }
}

function assignDefaultNicknameIfMissing(internalPlayerId) {
  if (!internalPlayerId) {
    return;
  }

  const db = openDatabase();
  const row = db
    .prepare(`SELECT nickname FROM player_profiles WHERE player_id = ?`)
    .get(internalPlayerId);
  if (!row || (typeof row.nickname === "string" && row.nickname.trim())) {
    return;
  }

  db.prepare(
    `UPDATE player_profiles
     SET nickname = ?, updated_at = ?
     WHERE player_id = ?`
  ).run(generateUniqueNickname(), nowMs(), internalPlayerId);
}

function ensurePlayer(externalPlayerId) {
  const normalizedExternalId = normalizeExternalPlayerId(externalPlayerId);
  let playerRow = getPlayerByExternalId(normalizedExternalId);
  if (playerRow) {
    if (!playerRow.nickname) {
      assignDefaultNicknameIfMissing(playerRow.id);
      playerRow = getPlayerByExternalId(normalizedExternalId);
    }
    if (!playerRow.starter_pack_granted) {
      grantStarterPack(playerRow.id);
      playerRow = getPlayerByExternalId(normalizedExternalId);
    }
    syncPlayerAchievements(playerRow.id);
    ensurePlayerMatchStats(playerRow.id);
    return buildProfileResponse(playerRow);
  }

  const db = openDatabase();
  const createdAt = nowMs();
  const playerId = newId();

  const createPlayer = db.transaction(() => {
    db.prepare(
      `INSERT INTO players (id, external_player_id, created_at, updated_at)
       VALUES (?, ?, ?, ?)`
    ).run(playerId, normalizedExternalId, createdAt, createdAt);

    db.prepare(
      `INSERT INTO player_profiles (
         player_id, nickname, selected_character_model, currency_balance,
         starter_pack_granted, updated_at
       ) VALUES (?, ?, NULL, 0, 0, ?)`
    ).run(playerId, generateUniqueNickname(), createdAt);

    grantStarterPack(playerId);
  });

  createPlayer();
  playerRow = getPlayerByExternalId(normalizedExternalId);
  syncPlayerAchievements(playerRow.id);
  ensurePlayerMatchStats(playerRow.id);
  return buildProfileResponse(playerRow);
}

function grantStarterPack(playerId) {
  const db = openDatabase();
  const timestamp = nowMs();

  const grant = db.transaction(() => {
    db.prepare(
      `UPDATE player_profiles
       SET currency_balance = currency_balance + ?,
           starter_pack_granted = 1,
           updated_at = ?
       WHERE player_id = ?`
    ).run(STARTER_CURRENCY, timestamp, playerId);

    const insertOwned = db.prepare(
      `INSERT OR IGNORE INTO player_owned_items
       (player_id, item_type, item_id, source, acquired_at)
       VALUES (?, ?, ?, ?, ?)`
    );

    for (const skinId of DEFAULT_OWNED_SKIN_IDS) {
      insertOwned.run(playerId, ITEM_TYPE_SKIN, skinId, "starter", timestamp);
    }

    const upsertEquipped = db.prepare(
      `INSERT INTO player_equipped_items (player_id, slot_key, item_id, updated_at)
       VALUES (?, ?, ?, ?)
       ON CONFLICT(player_id, slot_key) DO UPDATE SET
         item_id = excluded.item_id,
         updated_at = excluded.updated_at`
    );

    for (const slot of EQUIPMENT_SLOTS) {
      upsertEquipped.run(playerId, slot, DEFAULT_EQUIPPED[slot] || "", timestamp);
    }
  });

  grant();
}

function purchaseSkin(externalPlayerId, skinId) {
  const normalizedSkinId = String(skinId || "").trim();
  if (!isKnownSkinId(normalizedSkinId)) {
    return { ok: false, error: "UnknownSkin", message: "Unknown skin id." };
  }
  if (isDefaultOwnedSkinId(normalizedSkinId)) {
    return { ok: false, error: "NotForSale", message: "Skin is not sold in shop." };
  }

  const playerRow = getPlayerByExternalId(externalPlayerId);
  if (!playerRow) {
    return { ok: false, error: "PlayerNotFound", message: "Player profile not found." };
  }

  const owned = getOwnedSkinIds(playerRow.id);
  if (owned.includes(normalizedSkinId)) {
    return { ok: false, error: "AlreadyOwned", message: "Skin already owned." };
  }

  const price = getShopPrice(normalizedSkinId);
  if (price <= 0) {
    return { ok: false, error: "NotForSale", message: "Skin is not sold in shop." };
  }

  const db = openDatabase();
  const timestamp = nowMs();

  try {
    const purchase = db.transaction(() => {
      const profile = db
        .prepare(
          `SELECT currency_balance
           FROM player_profiles
           WHERE player_id = ?`
        )
        .get(playerRow.id);

      if (!profile || profile.currency_balance < price) {
        throw new Error("INSUFFICIENT_FUNDS");
      }

      db.prepare(
        `UPDATE player_profiles
         SET currency_balance = currency_balance - ?,
             updated_at = ?
         WHERE player_id = ?`
      ).run(price, timestamp, playerRow.id);

      db.prepare(
        `INSERT INTO player_owned_items
         (player_id, item_type, item_id, source, acquired_at)
         VALUES (?, ?, ?, ?, ?)`
      ).run(playerRow.id, ITEM_TYPE_SKIN, normalizedSkinId, "purchase", timestamp);
    });

    purchase();
  } catch (error) {
    if (String(error.message || error) === "INSUFFICIENT_FUNDS") {
      return { ok: false, error: "InsufficientFunds", message: "Not enough currency." };
    }
    throw error;
  }

  return { ok: true, profile: buildProfileResponse(getPlayerByExternalId(externalPlayerId)) };
}

function purchaseCase(externalPlayerId, caseId) {
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

  const playerRow = getPlayerByExternalId(externalPlayerId);
  if (!playerRow) {
    return { ok: false, error: "PlayerNotFound", message: "Player profile not found." };
  }

  const db = openDatabase();
  const timestamp = nowMs();

  try {
    const purchase = db.transaction(() => {
      const profile = db
        .prepare(
          `SELECT currency_balance
           FROM player_profiles
           WHERE player_id = ?`
        )
        .get(playerRow.id);

      if (!profile || profile.currency_balance < price) {
        throw new Error("INSUFFICIENT_FUNDS");
      }

      db.prepare(
        `UPDATE player_profiles
         SET currency_balance = currency_balance - ?,
             updated_at = ?
         WHERE player_id = ?`
      ).run(price, timestamp, playerRow.id);

      if (!grantOwnedCase(playerRow.id, normalizedCaseId, "purchase", true)) {
        throw new Error("GRANT_FAILED");
      }
    });

    purchase();
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
    profile: buildProfileResponse(getPlayerByExternalId(externalPlayerId)),
  };
}

function openCase(externalPlayerId, caseId) {
  const normalizedCaseId = String(caseId || "").trim();
  const caseDefinition = getCaseDefinition(normalizedCaseId);
  if (!caseDefinition) {
    return { ok: false, error: "UnknownCase", message: "Unknown case id." };
  }

  if (!Array.isArray(caseDefinition.lootPool) || caseDefinition.lootPool.length === 0) {
    return { ok: false, error: "EmptyCase", message: "Case loot pool is empty." };
  }

  const playerRow = getPlayerByExternalId(externalPlayerId);
  if (!playerRow) {
    return { ok: false, error: "PlayerNotFound", message: "Player profile not found." };
  }

  const rolledSkinId = rollCaseLoot(normalizedCaseId);
  if (!rolledSkinId || !isKnownSkinId(rolledSkinId)) {
    return { ok: false, error: "RollFailed", message: "Failed to roll case reward." };
  }

  const db = openDatabase();
  const timestamp = nowMs();

  try {
    const open = db.transaction(() => {
      if (!consumeOwnedCase(playerRow.id, normalizedCaseId)) {
        throw new Error("CASE_NOT_OWNED");
      }

      if (!grantOwnedSkin(playerRow.id, rolledSkinId, "case_open", true)) {
        throw new Error("GRANT_FAILED");
      }

      db.prepare(
        `UPDATE player_profiles
         SET updated_at = ?
         WHERE player_id = ?`
      ).run(timestamp, playerRow.id);
    });

    open();
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
    profile: buildProfileResponse(getPlayerByExternalId(externalPlayerId)),
  };
}

function setEquippedSlot(externalPlayerId, slotKey, itemId) {
  const normalizedSlot = String(slotKey || "").trim().toLowerCase();
  if (!EQUIPMENT_SLOTS.includes(normalizedSlot)) {
    return { ok: false, error: "InvalidSlot", message: "Unknown equipment slot." };
  }

  const playerRow = getPlayerByExternalId(externalPlayerId);
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

    const owned = getOwnedSkinIds(playerRow.id);
    if (!owned.includes(normalizedItemId)) {
      return { ok: false, error: "NotOwned", message: "Skin is not owned." };
    }
  }

  const db = openDatabase();
  db.prepare(
    `INSERT INTO player_equipped_items (player_id, slot_key, item_id, updated_at)
     VALUES (?, ?, ?, ?)
     ON CONFLICT(player_id, slot_key) DO UPDATE SET
       item_id = excluded.item_id,
       updated_at = excluded.updated_at`
  ).run(playerRow.id, normalizedSlot, normalizedItemId, nowMs());

  return { ok: true, profile: buildProfileResponse(getPlayerByExternalId(externalPlayerId)) };
}

function setNickname(externalPlayerId, nickname) {
  const normalizedNickname = normalizeNickname(nickname);
  if (!normalizedNickname) {
    return { ok: false, error: "InvalidNickname", message: "Nickname is empty or invalid." };
  }

  const playerRow = getPlayerByExternalId(externalPlayerId);
  if (!playerRow) {
    return { ok: false, error: "PlayerNotFound", message: "Player profile not found." };
  }

  if (findPlayerIdByNickname(normalizedNickname, playerRow.id)) {
    return { ok: false, error: "NicknameTaken", message: "Nickname is already taken." };
  }

  const db = openDatabase();
  const timestamp = nowMs();
  try {
    const update = db.transaction(() => {
      db.prepare(
        `UPDATE player_profiles
         SET nickname = ?, updated_at = ?
         WHERE player_id = ?`
      ).run(normalizedNickname, timestamp, playerRow.id);

      db.prepare(
        `INSERT INTO player_nickname_history (player_id, nickname, changed_at)
         VALUES (?, ?, ?)`
      ).run(playerRow.id, normalizedNickname, timestamp);
    });
    update();
  } catch (error) {
    if (String(error.message || error).includes("UNIQUE")) {
      return { ok: false, error: "NicknameTaken", message: "Nickname is already taken." };
    }

    throw error;
  }

  return { ok: true, profile: buildProfileResponse(getPlayerByExternalId(externalPlayerId)) };
}

function setSelectedCharacterModel(externalPlayerId, modelName) {
  const normalizedModel = typeof modelName === "string" ? modelName.trim().slice(0, 64) : "";
  const playerRow = getPlayerByExternalId(externalPlayerId);
  if (!playerRow) {
    return { ok: false, error: "PlayerNotFound", message: "Player profile not found." };
  }

  const db = openDatabase();
  db.prepare(
    `UPDATE player_profiles
     SET selected_character_model = ?, updated_at = ?
     WHERE player_id = ?`
  ).run(normalizedModel, nowMs(), playerRow.id);

  return { ok: true, profile: buildProfileResponse(getPlayerByExternalId(externalPlayerId)) };
}

function listPlayerAchievements(playerId) {
  const db = openDatabase();
  const rows = db
    .prepare(
      `SELECT pa.achievement_id, pa.progress, pa.target, pa.completed_at, pa.claimed_at,
              ad.code, ad.title, ad.description, ad.category, ad.is_hidden
       FROM player_achievements pa
       JOIN achievement_definitions ad ON ad.id = pa.achievement_id
       WHERE pa.player_id = ? AND pa.claimed_at IS NULL
       ORDER BY ad.sort_order ASC, ad.title ASC`
    )
    .all(playerId);

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

function reportAchievementEvent(externalPlayerId, eventType, amount) {
  const normalizedEventType = String(eventType || "").trim();
  if (!normalizedEventType) {
    return { ok: false, error: "MissingEventType", message: "Event type is required." };
  }

  const increment = Math.max(1, Math.floor(Number(amount) || 1));
  const playerRow = getPlayerByExternalId(externalPlayerId);
  if (!playerRow) {
    return { ok: false, error: "PlayerNotFound", message: "Player profile not found." };
  }

  syncPlayerAchievements(playerRow.id);
  const matching = getAchievementsByEventType(normalizedEventType);
  if (!Array.isArray(matching) || matching.length === 0) {
    return {
      ok: true,
      newlyCompleted: [],
      profile: buildProfileResponse(playerRow),
    };
  }

  const db = openDatabase();
  const timestamp = nowMs();
  const newlyCompleted = [];

  const report = db.transaction(() => {
    for (let i = 0; i < matching.length; i++) {
      const definition = matching[i];
      const row = db
        .prepare(
          `SELECT progress, target, completed_at, claimed_at
           FROM player_achievements
           WHERE player_id = ? AND achievement_id = ?`
        )
        .get(playerRow.id, definition.achievementId);

      if (!row || row.completed_at || row.claimed_at) {
        continue;
      }

      const target = Math.max(1, Number(row.target) || definition.target || 1);
      const nextProgress = Math.min(target, (Number(row.progress) || 0) + increment);
      const completedAt = nextProgress >= target ? timestamp : null;

      db.prepare(
        `UPDATE player_achievements
         SET progress = ?,
             target = ?,
             completed_at = COALESCE(completed_at, ?),
             updated_at = ?
         WHERE player_id = ? AND achievement_id = ?`
      ).run(nextProgress, target, completedAt, timestamp, playerRow.id, definition.achievementId);

      if (completedAt && !row.completed_at) {
        newlyCompleted.push({
          achievementId: definition.achievementId,
          title: definition.title,
          description: definition.description || "",
        });
      }
    }
  });

  report();

  return {
    ok: true,
    newlyCompleted,
    profile: buildProfileResponse(getPlayerByExternalId(externalPlayerId)),
  };
}

function claimAchievement(externalPlayerId, achievementId) {
  const normalizedAchievementId = String(achievementId || "").trim();
  const definition = getAchievementDefinition(normalizedAchievementId);
  if (!definition) {
    return { ok: false, error: "UnknownAchievement", message: "Unknown achievement id." };
  }

  const playerRow = getPlayerByExternalId(externalPlayerId);
  if (!playerRow) {
    return { ok: false, error: "PlayerNotFound", message: "Player profile not found." };
  }

  const db = openDatabase();
  const timestamp = nowMs();
  const row = db
    .prepare(
      `SELECT completed_at, claimed_at
       FROM player_achievements
       WHERE player_id = ? AND achievement_id = ?`
    )
    .get(playerRow.id, normalizedAchievementId);

  if (!row || !row.completed_at) {
    return { ok: false, error: "NotCompleted", message: "Achievement is not completed yet." };
  }

  if (row.claimed_at) {
    return { ok: false, error: "AlreadyClaimed", message: "Achievement reward already claimed." };
  }

  try {
    const claim = db.transaction(() => {
      if (definition.reward.type === "currency") {
        db.prepare(
          `UPDATE player_profiles
           SET currency_balance = currency_balance + ?,
               updated_at = ?
           WHERE player_id = ?`
        ).run(definition.reward.amount, timestamp, playerRow.id);
      } else if (definition.reward.type === "case") {
        for (let i = 0; i < definition.reward.amount; i++) {
          if (!grantOwnedCase(playerRow.id, definition.reward.caseId, "achievement", true)) {
            throw new Error("GRANT_FAILED");
          }
        }
      } else {
        throw new Error("UNKNOWN_REWARD");
      }

      db.prepare(
        `UPDATE player_achievements
         SET claimed_at = ?,
             updated_at = ?
         WHERE player_id = ? AND achievement_id = ?`
      ).run(timestamp, timestamp, playerRow.id, normalizedAchievementId);
    });

    claim();
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
    profile: buildProfileResponse(getPlayerByExternalId(externalPlayerId)),
  };
}

function listPlayerRewardClaims(playerId) {
  const db = openDatabase();
  const rows = db
    .prepare(
      `SELECT prc.reward_id, prc.source_type, prc.source_id, prc.claimed_at,
              rd.code, rd.reward_type, rd.payload_json
       FROM player_reward_claims prc
       JOIN reward_definitions rd ON rd.id = prc.reward_id
       WHERE prc.player_id = ?
       ORDER BY prc.claimed_at DESC`
    )
    .all(playerId);

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

function playerOwnsSkin(externalPlayerId, skinId) {
  const playerRow = getPlayerByExternalId(externalPlayerId);
  if (!playerRow) {
    return false;
  }
  const normalizedSkinId = String(skinId || "").trim();
  if (!normalizedSkinId) {
    return false;
  }
  const owned = getOwnedSkinIds(playerRow.id);
  return owned.includes(normalizedSkinId);
}

function grantMatchCurrency(externalPlayerId, amount, sourceId) {
  const normalizedAmount = Math.floor(Number(amount));
  if (!Number.isFinite(normalizedAmount) || normalizedAmount <= 0) {
    return { ok: false, error: "InvalidAmount", message: "Reward amount must be positive." };
  }

  const normalizedSourceId = String(sourceId || "").trim();
  if (!normalizedSourceId) {
    return { ok: false, error: "MissingSourceId", message: "Match source id is required." };
  }

  const playerRow = getPlayerByExternalId(externalPlayerId);
  if (!playerRow) {
    return { ok: false, error: "PlayerNotFound", message: "Player profile not found." };
  }

  const db = openDatabase();
  const timestamp = nowMs();
  const grantType = "match_reward";

  try {
    const alreadyGranted = db.transaction(() => {
      const existing = db
        .prepare(
          `SELECT id
           FROM player_currency_grants
           WHERE player_id = ? AND grant_type = ? AND source_id = ?`
        )
        .get(playerRow.id, grantType, normalizedSourceId);

      if (existing) {
        return true;
      }

      db.prepare(
        `UPDATE player_profiles
         SET currency_balance = currency_balance + ?,
             updated_at = ?
         WHERE player_id = ?`
      ).run(normalizedAmount, timestamp, playerRow.id);

      db.prepare(
        `INSERT INTO player_currency_grants
         (player_id, grant_type, source_id, amount, granted_at)
         VALUES (?, ?, ?, ?, ?)`
      ).run(playerRow.id, grantType, normalizedSourceId, normalizedAmount, timestamp);

      return false;
    })();

    return {
      ok: true,
      alreadyGranted,
      profile: buildProfileResponse(getPlayerByExternalId(externalPlayerId)),
    };
  } catch (error) {
    return {
      ok: false,
      error: "GrantFailed",
      message: error && error.message ? error.message : "Failed to grant match reward.",
    };
  }
}

function getLeaderboard(limit = 25) {
  const db = openDatabase();
  const normalizedLimit = Math.max(1, Math.min(25, Math.floor(Number(limit) || 25)));
  const rows = db
    .prepare(
      `SELECT COALESCE(NULLIF(TRIM(pp.nickname), ''), 'Игрок') AS nickname,
              pp.rating,
              p.external_player_id AS player_id
       FROM player_profiles pp
       INNER JOIN players p ON p.id = pp.player_id
       ORDER BY pp.rating DESC, pp.updated_at ASC
       LIMIT ?`
    )
    .all(normalizedLimit);

  return rows.map((row, index) => ({
    rank: index + 1,
    nickname: row.nickname || "Игрок",
    rating: Number.isFinite(row.rating) ? Math.max(0, row.rating) : 1000,
    playerId: row.player_id || "",
  }));
}

module.exports = {
  ensurePlayer,
  getProfile: (externalPlayerId) => {
    const playerRow = getPlayerByExternalId(externalPlayerId);
    if (!playerRow) {
      return null;
    }
    if (!playerRow.starter_pack_granted) {
      grantStarterPack(playerRow.id);
    }
    syncPlayerAchievements(playerRow.id);
    ensurePlayerMatchStats(playerRow.id);
    return buildProfileResponse(getPlayerByExternalId(externalPlayerId));
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
