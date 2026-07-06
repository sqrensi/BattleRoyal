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
    await this.applyModeLeaderboardMigration();
    await this.applyModeStatsLeaderboardMigration();
    await this.applyVipPrefixMigration();
    await this.applyVipPrefixExpiryMigration();
    await this.applyNoAdsMigration();
    await this.applyClientStateMigration();
    await this.syncAchievementDefinitions();
  }

  async applyClientStateMigration() {
    const migrationName = "012_client_state";
    const applied = await this.get(
      "SELECT 1 AS ok FROM schema_migrations WHERE name = ?",
      [migrationName]
    );
    if (applied) {
      return;
    }

    await this.pool.query(
      "ALTER TABLE player_profiles ADD COLUMN IF NOT EXISTS client_state_json TEXT NOT NULL DEFAULT '{}'"
    );
    await this.run(
      "INSERT INTO schema_migrations (name, applied_at) VALUES (?, ?) ON CONFLICT (name) DO NOTHING",
      [migrationName, Date.now()]
    );
  }

  async applyNoAdsMigration() {
    const migrationName = "011_no_ads";
    const applied = await this.get(
      "SELECT 1 AS ok FROM schema_migrations WHERE name = ?",
      [migrationName]
    );
    if (applied) {
      return;
    }

    await this.pool.query(
      "ALTER TABLE player_profiles ADD COLUMN IF NOT EXISTS no_ads_expires_at BIGINT NOT NULL DEFAULT 0"
    );
    await this.run(
      "INSERT INTO schema_migrations (name, applied_at) VALUES (?, ?) ON CONFLICT (name) DO NOTHING",
      [migrationName, Date.now()]
    );
  }

  async applyVipPrefixExpiryMigration() {
    const migrationName = "010_vip_prefix_expiry";
    const applied = await this.get(
      "SELECT 1 AS ok FROM schema_migrations WHERE name = ?",
      [migrationName]
    );
    if (applied) {
      return;
    }

    await this.pool.query(
      "ALTER TABLE player_profiles ADD COLUMN IF NOT EXISTS vip_prefix_expires_at BIGINT NOT NULL DEFAULT 0"
    );

    const migrationNow = Date.now();
    const graceExpiresAt = migrationNow + 30 * 24 * 60 * 60 * 1000;
    await this.pool.query(
      `UPDATE player_profiles
       SET vip_prefix_expires_at = $1
       WHERE has_vip_prefix = 1
         AND (vip_prefix_expires_at IS NULL OR vip_prefix_expires_at <= 0)`,
      [graceExpiresAt]
    );
    await this.run(
      "INSERT INTO schema_migrations (name, applied_at) VALUES (?, ?) ON CONFLICT (name) DO NOTHING",
      [migrationName, migrationNow]
    );
  }

  async applyVipPrefixMigration() {
    const migrationName = "009_vip_prefix";
    const applied = await this.get(
      "SELECT 1 AS ok FROM schema_migrations WHERE name = ?",
      [migrationName]
    );
    if (applied) {
      return;
    }

    await this.pool.query(
      "ALTER TABLE player_profiles ADD COLUMN IF NOT EXISTS has_vip_prefix INTEGER NOT NULL DEFAULT 0"
    );
    await this.run(
      "INSERT INTO schema_migrations (name, applied_at) VALUES (?, ?) ON CONFLICT (name) DO NOTHING",
      [migrationName, Date.now()]
    );
  }

  async applyModeStatsLeaderboardMigration() {
    const migrationName = "008_mode_stats_leaderboards";
    const applied = await this.get(
      "SELECT 1 AS ok FROM schema_migrations WHERE name = ?",
      [migrationName]
    );
    if (applied) {
      return;
    }

    await this.pool.query(
      "ALTER TABLE player_match_stats ADD COLUMN IF NOT EXISTS dm_total_kills INTEGER NOT NULL DEFAULT 0"
    );
    await this.pool.query(
      "ALTER TABLE player_match_stats ADD COLUMN IF NOT EXISTS dm_total_deaths INTEGER NOT NULL DEFAULT 0"
    );
    await this.pool.query(
      "ALTER TABLE player_match_stats ADD COLUMN IF NOT EXISTS training_time_seconds INTEGER NOT NULL DEFAULT 0"
    );
    await this.run(
      "INSERT INTO schema_migrations (name, applied_at) VALUES (?, ?) ON CONFLICT (name) DO NOTHING",
      [migrationName, Date.now()]
    );
  }

  async applyModeLeaderboardMigration() {
    const migrationName = "007_mode_leaderboards";
    const applied = await this.get(
      "SELECT 1 AS ok FROM schema_migrations WHERE name = ?",
      [migrationName]
    );
    if (applied) {
      return;
    }

    await this.pool.query(
      "ALTER TABLE player_profiles ADD COLUMN IF NOT EXISTS duel_rating INTEGER NOT NULL DEFAULT 1000"
    );
    await this.pool.query(
      "ALTER TABLE player_profiles ADD COLUMN IF NOT EXISTS challenge_best_time_ms INTEGER"
    );
    await this.pool.query(
      "UPDATE player_profiles SET duel_rating = 1000 WHERE duel_rating IS NULL OR duel_rating < 0"
    );
    await this.run(
      "INSERT INTO schema_migrations (name, applied_at) VALUES (?, ?) ON CONFLICT (name) DO NOTHING",
      [migrationName, Date.now()]
    );
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
