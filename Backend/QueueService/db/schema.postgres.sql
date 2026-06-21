CREATE TABLE IF NOT EXISTS schema_migrations (
  id SERIAL PRIMARY KEY,
  name TEXT NOT NULL UNIQUE,
  applied_at BIGINT NOT NULL
);

CREATE TABLE IF NOT EXISTS players (
  id TEXT PRIMARY KEY,
  external_player_id TEXT NOT NULL UNIQUE,
  created_at BIGINT NOT NULL,
  updated_at BIGINT NOT NULL
);

CREATE TABLE IF NOT EXISTS player_profiles (
  player_id TEXT PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE,
  nickname TEXT,
  selected_character_model TEXT,
  currency_balance INTEGER NOT NULL DEFAULT 0 CHECK (currency_balance >= 0),
  starter_pack_granted BOOLEAN NOT NULL DEFAULT FALSE,
  rating INTEGER NOT NULL DEFAULT 1000 CHECK (rating >= 0),
  updated_at BIGINT NOT NULL
);

CREATE TABLE IF NOT EXISTS player_owned_items (
  id SERIAL PRIMARY KEY,
  player_id TEXT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  item_type TEXT NOT NULL,
  item_id TEXT NOT NULL,
  source TEXT NOT NULL DEFAULT 'unknown',
  acquired_at BIGINT NOT NULL,
  quantity INTEGER NOT NULL DEFAULT 1 CHECK (quantity > 0),
  UNIQUE(player_id, item_type, item_id)
);

CREATE TABLE IF NOT EXISTS player_equipped_items (
  player_id TEXT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  slot_key TEXT NOT NULL,
  item_id TEXT,
  updated_at BIGINT NOT NULL,
  PRIMARY KEY (player_id, slot_key)
);

CREATE TABLE IF NOT EXISTS achievement_definitions (
  id TEXT PRIMARY KEY,
  code TEXT NOT NULL UNIQUE,
  title TEXT NOT NULL,
  description TEXT,
  category TEXT,
  sort_order INTEGER NOT NULL DEFAULT 0,
  is_hidden BOOLEAN NOT NULL DEFAULT FALSE,
  created_at BIGINT NOT NULL
);

CREATE TABLE IF NOT EXISTS player_achievements (
  player_id TEXT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  achievement_id TEXT NOT NULL REFERENCES achievement_definitions(id) ON DELETE CASCADE,
  progress INTEGER NOT NULL DEFAULT 0,
  target INTEGER NOT NULL DEFAULT 1,
  completed_at BIGINT,
  claimed_at BIGINT,
  updated_at BIGINT NOT NULL,
  PRIMARY KEY (player_id, achievement_id)
);

CREATE TABLE IF NOT EXISTS reward_definitions (
  id TEXT PRIMARY KEY,
  code TEXT NOT NULL UNIQUE,
  reward_type TEXT NOT NULL,
  payload_json TEXT NOT NULL,
  created_at BIGINT NOT NULL
);

CREATE TABLE IF NOT EXISTS player_reward_claims (
  id SERIAL PRIMARY KEY,
  player_id TEXT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  reward_id TEXT NOT NULL REFERENCES reward_definitions(id) ON DELETE CASCADE,
  source_type TEXT NOT NULL,
  source_id TEXT,
  claimed_at BIGINT NOT NULL,
  UNIQUE(player_id, reward_id, source_type, source_id)
);

CREATE TABLE IF NOT EXISTS player_nickname_history (
  id SERIAL PRIMARY KEY,
  player_id TEXT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  nickname TEXT NOT NULL,
  changed_at BIGINT NOT NULL
);

CREATE TABLE IF NOT EXISTS player_currency_grants (
  id SERIAL PRIMARY KEY,
  player_id TEXT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  grant_type TEXT NOT NULL,
  source_id TEXT NOT NULL,
  amount INTEGER NOT NULL CHECK (amount > 0),
  granted_at BIGINT NOT NULL,
  UNIQUE(player_id, grant_type, source_id)
);

CREATE TABLE IF NOT EXISTS player_match_stats (
  player_id TEXT PRIMARY KEY REFERENCES players(id) ON DELETE CASCADE,
  match_count INTEGER NOT NULL DEFAULT 0 CHECK (match_count >= 0),
  total_kills INTEGER NOT NULL DEFAULT 0 CHECK (total_kills >= 0),
  total_deaths INTEGER NOT NULL DEFAULT 0 CHECK (total_deaths >= 0),
  total_wins INTEGER NOT NULL DEFAULT 0 CHECK (total_wins >= 0),
  total_placement_sum INTEGER NOT NULL DEFAULT 0 CHECK (total_placement_sum >= 0),
  total_damage INTEGER NOT NULL DEFAULT 0 CHECK (total_damage >= 0),
  updated_at BIGINT NOT NULL
);

CREATE TABLE IF NOT EXISTS player_match_stat_reports (
  id SERIAL PRIMARY KEY,
  player_id TEXT NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  source_id TEXT NOT NULL,
  reported_at BIGINT NOT NULL,
  rating_delta INTEGER NOT NULL DEFAULT 0,
  UNIQUE(player_id, source_id)
);

CREATE INDEX IF NOT EXISTS idx_players_external_player_id ON players(external_player_id);
CREATE INDEX IF NOT EXISTS idx_player_owned_items_player_id ON player_owned_items(player_id);
CREATE INDEX IF NOT EXISTS idx_player_achievements_player_id ON player_achievements(player_id);
CREATE INDEX IF NOT EXISTS idx_player_reward_claims_player_id ON player_reward_claims(player_id);
CREATE INDEX IF NOT EXISTS idx_player_match_stat_reports_player_id ON player_match_stat_reports(player_id);
CREATE INDEX IF NOT EXISTS idx_player_profiles_rating ON player_profiles(rating DESC, updated_at ASC);

CREATE UNIQUE INDEX IF NOT EXISTS idx_player_profiles_nickname_unique
ON player_profiles (LOWER(nickname))
WHERE nickname IS NOT NULL AND nickname <> '';
