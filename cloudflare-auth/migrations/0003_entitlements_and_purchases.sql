CREATE TABLE IF NOT EXISTS entitlements (
  user_id TEXT PRIMARY KEY NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  plan TEXT NOT NULL DEFAULT 'free' CHECK (plan IN ('free', 'subscriber', 'owner')),
  monthly_free_queries_used INTEGER NOT NULL DEFAULT 0,
  one_off_queries_available INTEGER NOT NULL DEFAULT 0,
  updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS purchases (
  id TEXT PRIMARY KEY NOT NULL,
  user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  provider TEXT NOT NULL,
  provider_purchase_id TEXT NOT NULL,
  product_id TEXT NOT NULL,
  status TEXT NOT NULL,
  purchased_at TEXT NOT NULL,
  expires_at TEXT,
  UNIQUE(provider, provider_purchase_id)
);

CREATE INDEX IF NOT EXISTS idx_purchases_user_id ON purchases(user_id);
