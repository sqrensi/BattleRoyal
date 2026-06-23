const fs = require("fs");
const path = require("path");
const crypto = require("crypto");
const { resolveDriver } = require("./sqlDialect");
const { PostgresDriver } = require("./postgresDriver");

let Database;
try {
  Database = require("better-sqlite3");
} catch (error) {
  Database = null;
}

const DEFAULT_DB_PATH = path.join(__dirname, "..", "data", "shooterprototype.db");
const SCHEMA_PATH = path.join(__dirname, "schema.sql");

let sqliteDb = null;
let postgresDriver = null;
let activeDriver = "sqlite";

function nowMs() {
  return Date.now();
}

function newId() {
  return crypto.randomUUID();
}

function applySqliteMigrations(db) {
  db.exec(fs.readFileSync(SCHEMA_PATH, "utf8"));

  const insertMigration = db.prepare(
    "INSERT OR IGNORE INTO schema_migrations (name, applied_at) VALUES (?, ?)"
  );
  insertMigration.run("001_initial_schema", nowMs());
  applyOwnedItemQuantityMigration(db, insertMigration);
  applyAchievementDefinitionSyncMigration(db, insertMigration);
  applyPlayerMatchStatsMigration(db, insertMigration);
  applyPlayerAchievementClaimedAtMigration(db, insertMigration);
  applyPlayerRatingMigration(db, insertMigration);
  applyModeLeaderboardMigration(db, insertMigration);
}

function applyModeLeaderboardMigration(db, insertMigration) {
  const migrationName = "007_mode_leaderboards";
  const applied = db
    .prepare("SELECT 1 AS ok FROM schema_migrations WHERE name = ?")
    .get(migrationName);
  if (applied) {
    return;
  }

  const profileColumns = db.prepare("PRAGMA table_info(player_profiles)").all();
  const hasDuelRating = profileColumns.some((column) => column.name === "duel_rating");
  if (!hasDuelRating) {
    db.exec(
      "ALTER TABLE player_profiles ADD COLUMN duel_rating INTEGER NOT NULL DEFAULT 1000;"
    );
  }

  const hasChallengeBestTime = profileColumns.some(
    (column) => column.name === "challenge_best_time_ms"
  );
  if (!hasChallengeBestTime) {
    db.exec("ALTER TABLE player_profiles ADD COLUMN challenge_best_time_ms INTEGER;");
  }

  db.exec("UPDATE player_profiles SET duel_rating = 1000 WHERE duel_rating IS NULL OR duel_rating < 0;");

  insertMigration.run(migrationName, nowMs());
}

function applyPlayerRatingMigration(db, insertMigration) {
  const migrationName = "006_player_rating";
  const applied = db
    .prepare("SELECT 1 AS ok FROM schema_migrations WHERE name = ?")
    .get(migrationName);
  if (applied) {
    return;
  }

  const profileColumns = db.prepare("PRAGMA table_info(player_profiles)").all();
  const hasRating = profileColumns.some((column) => column.name === "rating");
  if (!hasRating) {
    db.exec(
      "ALTER TABLE player_profiles ADD COLUMN rating INTEGER NOT NULL DEFAULT 1000;"
    );
  }

  const reportColumns = db.prepare("PRAGMA table_info(player_match_stat_reports)").all();
  const hasRatingDelta = reportColumns.some((column) => column.name === "rating_delta");
  if (!hasRatingDelta) {
    db.exec(
      "ALTER TABLE player_match_stat_reports ADD COLUMN rating_delta INTEGER NOT NULL DEFAULT 0;"
    );
  }

  db.exec("UPDATE player_profiles SET rating = 1000 WHERE rating IS NULL OR rating < 0;");

  insertMigration.run(migrationName, nowMs());
}

function applyPlayerMatchStatsMigration(db, insertMigration) {
  const migrationName = "004_player_match_stats";
  const applied = db
    .prepare("SELECT 1 AS ok FROM schema_migrations WHERE name = ?")
    .get(migrationName);
  if (applied) {
    return;
  }

  db.exec(`
    CREATE TABLE IF NOT EXISTS player_match_stats (
      player_id TEXT PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE,
      match_count INTEGER NOT NULL DEFAULT 0 CHECK (match_count >= 0),
      total_kills INTEGER NOT NULL DEFAULT 0 CHECK (total_kills >= 0),
      total_deaths INTEGER NOT NULL DEFAULT 0 CHECK (total_deaths >= 0),
      total_wins INTEGER NOT NULL DEFAULT 0 CHECK (total_wins >= 0),
      total_placement_sum INTEGER NOT NULL DEFAULT 0 CHECK (total_placement_sum >= 0),
      total_damage INTEGER NOT NULL DEFAULT 0 CHECK (total_damage >= 0),
      updated_at INTEGER NOT NULL
    );

    CREATE TABLE IF NOT EXISTS player_match_stat_reports (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      player_id TEXT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
      source_id TEXT NOT NULL,
      reported_at INTEGER NOT NULL,
      UNIQUE(player_id, source_id)
    );

    CREATE INDEX IF NOT EXISTS idx_player_match_stat_reports_player_id
    ON player_match_stat_reports(player_id);
  `);

  insertMigration.run(migrationName, nowMs());
}

function applyAchievementDefinitionSyncMigration(db, insertMigration) {
  const migrationName = "003_sync_achievement_definitions";
  const applied = db
    .prepare("SELECT 1 AS ok FROM schema_migrations WHERE name = ?")
    .get(migrationName);
  if (applied) {
    syncAchievementDefinitions(db);
    return;
  }

  syncAchievementDefinitions(db);
  insertMigration.run(migrationName, nowMs());
}

function syncAchievementDefinitions(db) {
  let getAllAchievements;
  try {
    ({ getAllAchievements } = require("../data/achievement-catalog"));
  } catch (error) {
    return;
  }

  const achievements = getAllAchievements();
  if (!Array.isArray(achievements) || achievements.length === 0) {
    return;
  }

  const timestamp = nowMs();
  const upsert = db.prepare(
    `INSERT INTO achievement_definitions
     (id, code, title, description, category, sort_order, is_hidden, created_at)
     VALUES (?, ?, ?, ?, ?, ?, 0, ?)
     ON CONFLICT(id) DO UPDATE SET
       code = excluded.code,
       title = excluded.title,
       description = excluded.description,
       category = excluded.category,
       sort_order = excluded.sort_order`
  );

  for (let i = 0; i < achievements.length; i++) {
    const entry = achievements[i];
    if (!entry || !entry.achievementId) {
      continue;
    }

    upsert.run(
      entry.achievementId,
      entry.code || entry.achievementId,
      entry.title || entry.achievementId,
      entry.description || "",
      entry.eventType || "",
      Number.isFinite(entry.sortOrder) ? entry.sortOrder : 0,
      timestamp
    );
  }
}

function applyOwnedItemQuantityMigration(db, insertMigration) {
  const migrationName = "002_owned_item_quantity";
  const applied = db
    .prepare("SELECT 1 AS ok FROM schema_migrations WHERE name = ?")
    .get(migrationName);
  if (applied) {
    return;
  }

  const columns = db.prepare("PRAGMA table_info(player_owned_items)").all();
  const hasQuantity = columns.some((column) => column.name === "quantity");
  if (!hasQuantity) {
    db.exec(
      "ALTER TABLE player_owned_items ADD COLUMN quantity INTEGER NOT NULL DEFAULT 1 CHECK (quantity > 0)"
    );
  }

  insertMigration.run(migrationName, nowMs());
}

function applyPlayerAchievementClaimedAtMigration(db, insertMigration) {
  const migrationName = "005_player_achievement_claimed_at";
  const applied = db
    .prepare("SELECT 1 AS ok FROM schema_migrations WHERE name = ?")
    .get(migrationName);
  if (applied) {
    return;
  }

  const columns = db.prepare("PRAGMA table_info(player_achievements)").all();
  const hasClaimedAt = columns.some((column) => column.name === "claimed_at");
  if (!hasClaimedAt) {
    db.exec("ALTER TABLE player_achievements ADD COLUMN claimed_at INTEGER;");
  }

  insertMigration.run(migrationName, nowMs());
}

function openSqliteDatabase(dbPath = process.env.DATABASE_PATH || DEFAULT_DB_PATH) {
  if (sqliteDb) {
    return sqliteDb;
  }

  if (!Database) {
    throw new Error(
      "better-sqlite3 is not installed. Run `npm install` in Backend/QueueService."
    );
  }

  const dir = path.dirname(dbPath);
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }

  sqliteDb = new Database(dbPath);
  sqliteDb.pragma("journal_mode = WAL");
  sqliteDb.pragma("foreign_keys = ON");
  applySqliteMigrations(sqliteDb);
  return sqliteDb;
}

async function initDatabase() {
  activeDriver = resolveDriver();
  if (activeDriver === "postgres") {
    const connectionString = String(process.env.DATABASE_URL || "").trim();
    if (!connectionString) {
      throw new Error("DATABASE_URL is required when DB_DRIVER=postgres.");
    }

    postgresDriver = new PostgresDriver(connectionString);
    await postgresDriver.init();
    return {
      driver: "postgres",
      message: "[db] PostgreSQL profile database ready.",
    };
  }

  openSqliteDatabase();
  return {
    driver: "sqlite",
    message: "[db] SQLite profile database ready.",
  };
}

function getDriverName() {
  return activeDriver;
}

function openDatabase() {
  if (activeDriver === "postgres") {
    throw new Error("openDatabase() is sync-only for SQLite. Use initDatabase() and async db helpers.");
  }

  return openSqliteDatabase();
}

async function get(sql, params = []) {
  if (activeDriver === "postgres") {
    return postgresDriver.get(sql, params);
  }

  return openSqliteDatabase().prepare(sql).get(...params);
}

async function all(sql, params = []) {
  if (activeDriver === "postgres") {
    return postgresDriver.all(sql, params);
  }

  return openSqliteDatabase().prepare(sql).all(...params);
}

async function run(sql, params = []) {
  if (activeDriver === "postgres") {
    return postgresDriver.run(sql, params);
  }

  const result = openSqliteDatabase().prepare(sql).run(...params);
  return {
    changes: result.changes,
    lastInsertRowid: result.lastInsertRowid,
  };
}

async function transaction(work) {
  if (activeDriver === "postgres") {
    return postgresDriver.transaction(work);
  }

  const sqlite = openSqliteDatabase();
  const adapter = {
    get: (sql, params = []) => sqlite.prepare(sql).get(...params),
    all: (sql, params = []) => sqlite.prepare(sql).all(...params),
    run: (sql, params = []) => sqlite.prepare(sql).run(...params),
  };

  const asyncAdapter = {
    get: async (sql, params = []) => adapter.get(sql, params),
    all: async (sql, params = []) => adapter.all(sql, params),
    run: async (sql, params = []) => adapter.run(sql, params),
  };

  try {
    sqlite.exec("BEGIN IMMEDIATE");
    const result = await work(asyncAdapter);
    sqlite.exec("COMMIT");
    return result;
  } catch (error) {
    sqlite.exec("ROLLBACK");
    throw error;
  }
}

async function closeDatabase() {
  if (postgresDriver) {
    await postgresDriver.close();
    postgresDriver = null;
  }

  if (sqliteDb) {
    sqliteDb.close();
    sqliteDb = null;
  }
}

module.exports = {
  initDatabase,
  openDatabase,
  closeDatabase,
  getDriverName,
  get,
  all,
  run,
  transaction,
  nowMs,
  newId,
};
