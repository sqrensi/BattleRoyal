const fs = require("fs");
const path = require("path");
const crypto = require("crypto");

let Database;
try {
  Database = require("better-sqlite3");
} catch (error) {
  Database = null;
}

const DEFAULT_DB_PATH = path.join(__dirname, "..", "data", "shooterprototype.db");
const SCHEMA_PATH = path.join(__dirname, "schema.sql");

let dbInstance = null;

function nowMs() {
  return Date.now();
}

function newId() {
  return crypto.randomUUID();
}

function openDatabase(dbPath = process.env.DATABASE_PATH || DEFAULT_DB_PATH) {
  if (dbInstance) {
    return dbInstance;
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

  dbInstance = new Database(dbPath);
  dbInstance.pragma("journal_mode = WAL");
  dbInstance.pragma("foreign_keys = ON");
  applyMigrations(dbInstance);
  return dbInstance;
}

function applyMigrations(db) {
  db.exec(fs.readFileSync(SCHEMA_PATH, "utf8"));

  const insertMigration = db.prepare(
    "INSERT OR IGNORE INTO schema_migrations (name, applied_at) VALUES (?, ?)"
  );
  insertMigration.run("001_initial_schema", nowMs());
}

function closeDatabase() {
  if (dbInstance) {
    dbInstance.close();
    dbInstance = null;
  }
}

module.exports = {
  openDatabase,
  closeDatabase,
  nowMs,
  newId,
};
