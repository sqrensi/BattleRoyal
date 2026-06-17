const {
  DEFAULT_OWNED_SKIN_IDS,
  DEFAULT_EQUIPPED,
  EQUIPMENT_SLOTS,
  isKnownSkinId,
  isDefaultOwnedSkinId,
  getShopPrice,
  slotForSkinId,
} = require("../data/shop-catalog");
const crypto = require("crypto");
const { openDatabase, nowMs, newId } = require("./database");

const STARTER_CURRENCY = 100000;
const ITEM_TYPE_SKIN = "skin";
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
              pp.starter_pack_granted, pp.updated_at AS profile_updated_at
       FROM players p
       LEFT JOIN player_profiles pp ON pp.player_id = p.id
       WHERE p.external_player_id = ?`
    )
    .get(normalizeExternalPlayerId(externalPlayerId));
}

function getOwnedSkinIds(playerId) {
  const db = openDatabase();
  const rows = db
    .prepare(
      `SELECT item_id
       FROM player_owned_items
       WHERE player_id = ? AND item_type = ?
       ORDER BY item_id ASC`
    )
    .all(playerId, ITEM_TYPE_SKIN);
  return rows.map((row) => row.item_id);
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

function buildProfileResponse(playerRow) {
  if (!playerRow) {
    return null;
  }

  const ownedSkins = getOwnedSkinIds(playerRow.id);
  const equipped = getEquippedMap(playerRow.id);

  return {
    playerId: playerRow.external_player_id,
    internalPlayerId: playerRow.id,
    nickname: playerRow.nickname || "",
    selectedCharacterModel: playerRow.selected_character_model || "",
    currencyBalance: Number.isFinite(playerRow.currency_balance)
      ? playerRow.currency_balance
      : 0,
    starterPackGranted: !!playerRow.starter_pack_granted,
    ownedSkins,
    equipped,
    achievements: listPlayerAchievements(playerRow.id),
    claimedRewards: listPlayerRewardClaims(playerRow.id),
  };
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
      `SELECT pa.achievement_id, pa.progress, pa.target, pa.completed_at,
              ad.code, ad.title, ad.description, ad.category, ad.is_hidden
       FROM player_achievements pa
       JOIN achievement_definitions ad ON ad.id = pa.achievement_id
       WHERE pa.player_id = ?
       ORDER BY ad.sort_order ASC, ad.title ASC`
    )
    .all(playerId);

  return rows.map((row) => ({
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
  }));
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
    return buildProfileResponse(getPlayerByExternalId(externalPlayerId));
  },
  purchaseSkin,
  setEquippedSlot,
  setNickname,
  setSelectedCharacterModel,
  grantMatchCurrency,
  playerOwnsSkin,
  isNicknameAvailable,
  resolvePlayerNickname,
};
