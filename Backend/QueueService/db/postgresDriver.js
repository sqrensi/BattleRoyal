const fs = require("fs");
const path = require("path");
const { adaptSqlForDriver, appendInsertOrIgnoreSuffix } = require("./sqlDialect");

let Pool;
try {
  ({ Pool } = require("pg"));
} catch (error) {
  Pool = null;
}

const SCHEMA_PATH = path.join(__dirname, "schema.postgres.sql");

class PostgresDriver {
  constructor(connectionString) {
    if (!Pool) {
      throw new Error("pg is not installed. Run `npm install` in Backend/QueueService.");
    }

    this.pool = new Pool({
      connectionString,
      max: Math.max(4, Number(process.env.DB_POOL_MAX) || 20),
      idleTimeoutMillis: Math.max(1000, Number(process.env.DB_POOL_IDLE_MS) || 30000),
      connectionTimeoutMillis: Math.max(1000, Number(process.env.DB_POOL_CONNECT_MS) || 5000),
    });
  }

  async init() {
    const schemaSql = fs.readFileSync(SCHEMA_PATH, "utf8");
    await this.pool.query(schemaSql);
    await this.run(
      "INSERT INTO schema_migrations (name, applied_at) VALUES (?, ?) ON CONFLICT (name) DO NOTHING",
      ["001_initial_schema", Date.now()]
    );
    await this.syncAchievementDefinitions();
  }

  async syncAchievementDefinitions() {
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

    const timestamp = Date.now();
    for (let i = 0; i < achievements.length; i++) {
      const entry = achievements[i];
      if (!entry || !entry.achievementId) {
        continue;
      }

      await this.run(
        `INSERT INTO achievement_definitions
         (id, code, title, description, category, sort_order, is_hidden, created_at)
         VALUES (?, ?, ?, ?, ?, ?, FALSE, ?)
         ON CONFLICT(id) DO UPDATE SET
           code = excluded.code,
           title = excluded.title,
           description = excluded.description,
           category = excluded.category,
           sort_order = excluded.sort_order`,
        [
          entry.achievementId,
          entry.code || entry.achievementId,
          entry.title || entry.achievementId,
          entry.description || "",
          entry.eventType || "",
          Number.isFinite(entry.sortOrder) ? entry.sortOrder : 0,
          timestamp,
        ]
      );
    }
  }

  adapt(sql) {
    const withIgnore = appendInsertOrIgnoreSuffix(sql, "postgres");
    return adaptSqlForDriver(withIgnore, "postgres");
  }

  async get(sql, params = []) {
    const result = await this.pool.query(this.adapt(sql), params);
    return result.rows[0] || null;
  }

  async all(sql, params = []) {
    const result = await this.pool.query(this.adapt(sql), params);
    return result.rows;
  }

  async run(sql, params = []) {
    const result = await this.pool.query(this.adapt(sql), params);
    return {
      changes: result.rowCount || 0,
      lastInsertRowid: result.rows[0] && result.rows[0].id ? result.rows[0].id : undefined,
    };
  }

  async transaction(work) {
    const client = await this.pool.connect();
    try {
      await client.query("BEGIN");
      const tx = {
        get: async (sql, params = []) => {
          const result = await client.query(this.adapt(sql), params);
          return result.rows[0] || null;
        },
        all: async (sql, params = []) => {
          const result = await client.query(this.adapt(sql), params);
          return result.rows;
        },
        run: async (sql, params = []) => {
          const result = await client.query(this.adapt(sql), params);
          return { changes: result.rowCount || 0 };
        },
      };
      const value = await work(tx);
      await client.query("COMMIT");
      return value;
    } catch (error) {
      await client.query("ROLLBACK");
      throw error;
    } finally {
      client.release();
    }
  }

  async close() {
    await this.pool.end();
  }
}

module.exports = {
  PostgresDriver,
};
