INSERT INTO entitlements (user_id, plan, monthly_free_queries_used, one_off_queries_available, updated_at)
SELECT id,
       CASE WHEN role = 'owner' THEN 'owner' ELSE 'free' END,
       0,
       0,
       datetime('now')
FROM users
WHERE NOT EXISTS (
  SELECT 1
  FROM entitlements
  WHERE entitlements.user_id = users.id
);
