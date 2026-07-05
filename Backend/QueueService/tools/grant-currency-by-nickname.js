"use strict";

const crypto = require("crypto");
const { openDatabase, closeDatabase } = require("../db/database");

const nickname = process.argv[2];
const amount = Number.parseInt(process.argv[3], 10);
const createExternalId = getFlagValue("--external-id");

if (!nickname || !Number.isFinite(amount) || amount <= 0) {
  console.error(
    "Usage: node tools/grant-currency-by-nickname.js <nickname> <amount> [--external-id <playerId>]"
  );
  process.exit(1);
}

const db = openDatabase();
let player = findPlayerByNickname(db, nickname.trim());

if (!player && createExternalId) {
  player = ensurePlayerWithNickname(db, createExternalId.trim(), nickname.trim());
}

if (!player) {
  console.error(`Player with nickname '${nickname}' was not found.`);
  console.error("Tip: pass --external-id <yandexPlayerId> to create/update a profile.");
  closeDatabase().finally(() => process.exit(2));
  return;
}

const timestamp = Date.now();
const result = db
  .prepare(
    `UPDATE player_profiles
     SET currency_balance = currency_balance + ?,
         updated_at = ?
     WHERE player_id = ?`
  )
  .run(amount, timestamp, player.player_id);

const updated = db
  .prepare(`SELECT currency_balance FROM player_profiles WHERE player_id = ?`)
  .get(player.player_id);

closeDatabase().then(() => {
  console.log(
    JSON.stringify(
      {
        ok: true,
        nickname: player.nickname,
        externalId: player.external_player_id,
        added: amount,
        previousBalance: player.currency_balance,
        newBalance: updated ? updated.currency_balance : null,
        changes: result.changes,
      },
      null,
      2
    )
  );
});

function getFlagValue(flag) {
  const index = process.argv.indexOf(flag);
  if (index < 0 || index + 1 >= process.argv.length) {
    return "";
  }

  return process.argv[index + 1];
}

function findPlayerByNickname(dbConn, normalizedNickname) {
  let row = dbConn
    .prepare(
      `SELECT p.id AS player_id, p.external_player_id, pp.nickname, pp.currency_balance
       FROM players p
       JOIN player_profiles pp ON pp.player_id = p.id
       WHERE LOWER(pp.nickname) = LOWER(?)`
    )
    .get(normalizedNickname);

  if (row) {
    return row;
  }

  return (
    dbConn
      .prepare(
        `SELECT p.id AS player_id, p.external_player_id, pp.nickname, pp.currency_balance
         FROM players p
         JOIN player_profiles pp ON pp.player_id = p.id
         WHERE pp.nickname LIKE ?
         ORDER BY pp.updated_at DESC
         LIMIT 1`
      )
      .get(`%${normalizedNickname}%`) || null
  );
}

function ensurePlayerWithNickname(dbConn, externalPlayerId, normalizedNickname) {
  const timestamp = Date.now();
  let playerRow = dbConn
    .prepare(`SELECT id FROM players WHERE external_player_id = ?`)
    .get(externalPlayerId);

  if (!playerRow) {
    const playerId = crypto.randomUUID();
    dbConn
      .prepare(
        `INSERT INTO players (id, external_player_id, created_at, updated_at)
         VALUES (?, ?, ?, ?)`
      )
      .run(playerId, externalPlayerId, timestamp, timestamp);
    playerRow = { id: playerId };
  }

  const profileRow = dbConn
    .prepare(`SELECT player_id FROM player_profiles WHERE player_id = ?`)
    .get(playerRow.id);

  if (!profileRow) {
    dbConn
      .prepare(
        `INSERT INTO player_profiles
         (player_id, nickname, selected_character_model, currency_balance, starter_pack_granted, rating, updated_at)
         VALUES (?, ?, '', 0, 0, 1000, ?)`
      )
      .run(playerRow.id, normalizedNickname, timestamp);
  } else {
    dbConn
      .prepare(
        `UPDATE player_profiles
         SET nickname = ?, updated_at = ?
         WHERE player_id = ?`
      )
      .run(normalizedNickname, timestamp, playerRow.id);
  }

  return dbConn
    .prepare(
      `SELECT p.id AS player_id, p.external_player_id, pp.nickname, pp.currency_balance
       FROM players p
       JOIN player_profiles pp ON pp.player_id = p.id
       WHERE p.id = ?`
    )
    .get(playerRow.id);
}
