function resolveDriver() {
  if (process.env.DATABASE_URL && String(process.env.DATABASE_URL).trim()) {
    return "postgres";
  }

  const configured = String(process.env.DB_DRIVER || "sqlite").trim().toLowerCase();
  return configured === "postgres" ? "postgres" : "sqlite";
}

function toPostgresPlaceholders(sql) {
  let index = 0;
  return sql.replace(/\?/g, () => {
    index += 1;
    return `$${index}`;
  });
}

function adaptSqlForDriver(sql, driver) {
  if (driver !== "postgres") {
    return sql;
  }

  let adapted = String(sql || "");
  adapted = adapted.replace(
    /WHERE nickname = \? COLLATE NOCASE/gi,
    "WHERE LOWER(nickname) = LOWER(?)"
  );
  adapted = adapted.replace(/INSERT OR IGNORE INTO/gi, "INSERT INTO");
  adapted = adapted.replace(
    /UPDATE player_profiles\s+SET rating = MAX\(0, rating \+ \?\)/gi,
    "UPDATE player_profiles SET rating = GREATEST(0, rating + ?)"
  );
  adapted = adapted.replace(
    /completed_at = COALESCE\(completed_at, \?\)/gi,
    "completed_at = COALESCE(completed_at, ?)"
  );

  return toPostgresPlaceholders(adapted);
}

function appendInsertOrIgnoreSuffix(sql, driver) {
  if (driver !== "postgres") {
    return sql;
  }

  const normalized = String(sql || "").trim();
  if (!/^INSERT INTO/i.test(normalized)) {
    return normalized;
  }

  if (/ON CONFLICT/i.test(normalized)) {
    return normalized;
  }

  if (/INTO player_match_stats/i.test(normalized)) {
    return `${normalized} ON CONFLICT (player_id) DO NOTHING`;
  }

  if (/INTO player_achievements/i.test(normalized)) {
    return `${normalized} ON CONFLICT (player_id, achievement_id) DO NOTHING`;
  }

  if (/INTO player_owned_items/i.test(normalized)) {
    return `${normalized} ON CONFLICT (player_id, item_type, item_id) DO NOTHING`;
  }

  if (/INTO player_currency_grants/i.test(normalized)) {
    return `${normalized} ON CONFLICT (player_id, grant_type, source_id) DO NOTHING`;
  }

  if (/INTO player_match_stat_reports/i.test(normalized)) {
    return `${normalized} ON CONFLICT (player_id, source_id) DO NOTHING`;
  }

  if (/INTO schema_migrations/i.test(normalized)) {
    return `${normalized} ON CONFLICT (name) DO NOTHING`;
  }

  return `${normalized} ON CONFLICT DO NOTHING`;
}

module.exports = {
  resolveDriver,
  adaptSqlForDriver,
  appendInsertOrIgnoreSuffix,
  toPostgresPlaceholders,
};
