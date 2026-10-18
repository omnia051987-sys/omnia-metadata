CREATE TABLE IF NOT EXISTS password_credentials (
  user_id TEXT PRIMARY KEY NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  hash_b64 TEXT NOT NULL,
  salt_b64 TEXT NOT NULL,
  iterations INTEGER NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
