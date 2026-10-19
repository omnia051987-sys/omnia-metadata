export interface Env {
  DB: D1Database;
  INTERNAL_API_SECRET?: string;
}

type SessionInput = {
  token?: string;
  email?: string;
  subject?: string;
  provider?: string;
  role?: "user" | "owner";
  expiresUtc?: string;
};

type ConfirmationInput = {
  token?: string;
  email?: string;
  expiresUtc?: string;
};

type PasswordRegistrationInput = {
  email?: string;
  hashB64?: string;
  saltB64?: string;
  iterations?: number;
};

type PasswordLookupInput = {
  email?: string;
};

type EntitlementInput = {
  email?: string;
};

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    if (url.pathname === "/health")
      return Response.json({ service: "omnia-auth-store", status: "ok", persistent: true });

    if (!env.INTERNAL_API_SECRET || request.headers.get("Authorization") !== `Bearer ${env.INTERNAL_API_SECRET}`)
      return Response.json({ error: "Não autorizado." }, { status: 401 });

    if (request.method !== "POST")
      return Response.json({ error: "Método não permitido." }, { status: 405 });

    try {
      if (url.pathname === "/internal/session/create") return createSession(request, env);
      if (url.pathname === "/internal/session/get") return getSession(request, env);
      if (url.pathname === "/internal/session/revoke") return revokeSession(request, env);
      if (url.pathname === "/internal/confirmation/create") return createConfirmation(request, env);
      if (url.pathname === "/internal/confirmation/consume") return consumeConfirmation(request, env);
      if (url.pathname === "/internal/password/register") return registerPassword(request, env);
      if (url.pathname === "/internal/password/get") return getPassword(request, env);
      if (url.pathname === "/internal/entitlements/get") return getEntitlements(request, env);
      return Response.json({ error: "Não encontrado." }, { status: 404 });
    } catch (error) {
      console.error("D1 auth store error", error);
      return Response.json({ error: "Falha na persistência." }, { status: 500 });
    }
  }
};

async function createSession(request: Request, env: Env): Promise<Response> {
  const input = await request.json<SessionInput>();
  if (!input.token || !input.email || !input.subject || !input.provider || !input.role || !input.expiresUtc)
    return Response.json({ error: "Dados de sessão incompletos." }, { status: 400 });

  const now = new Date().toISOString();
  const userId = await sha256(`user:${input.email.toLowerCase()}`);
  const tokenHash = await sha256(`session:${input.token}`);
  await env.DB.batch([
    env.DB.prepare(`INSERT INTO users (id, email, email_verified, role, created_at, updated_at)
      VALUES (?, ?, 1, ?, ?, ?)
      ON CONFLICT(email) DO UPDATE SET email_verified = 1, role = CASE WHEN users.role = 'owner' THEN 'owner' ELSE excluded.role END, updated_at = excluded.updated_at`)
      .bind(userId, input.email.toLowerCase(), input.role, now, now),
    env.DB.prepare(`INSERT INTO identities (provider, provider_subject, user_id, created_at)
      VALUES (?, ?, ?, ?)
      ON CONFLICT(provider, provider_subject) DO UPDATE SET user_id = excluded.user_id`)
      .bind(input.provider, input.subject, userId, now),
    env.DB.prepare(`INSERT INTO entitlements
      (user_id, plan, monthly_free_queries_used, one_off_queries_available, updated_at)
      VALUES (?, ?, 0, 0, ?)
      ON CONFLICT(user_id) DO UPDATE SET
        plan = CASE WHEN entitlements.plan = 'owner' OR excluded.plan = 'owner' THEN 'owner' ELSE entitlements.plan END,
        updated_at = excluded.updated_at`)
      .bind(userId, input.role === "owner" ? "owner" : "free", now),
    env.DB.prepare(`INSERT OR REPLACE INTO auth_sessions (token_hash, user_id, provider, expires_at, revoked_at, created_at)
      VALUES (?, ?, ?, ?, NULL, ?)`)
      .bind(tokenHash, userId, input.provider, input.expiresUtc, now)
  ]);
  return Response.json({ ok: true });
}

async function getSession(request: Request, env: Env): Promise<Response> {
  const input = await request.json<SessionInput>();
  if (!input.token) return Response.json({ error: "Token não informado." }, { status: 400 });
  const tokenHash = await sha256(`session:${input.token}`);
  const row = await env.DB.prepare(`SELECT u.email, u.role, s.provider, s.expires_at
    FROM auth_sessions s JOIN users u ON u.id = s.user_id
    WHERE s.token_hash = ? AND s.revoked_at IS NULL AND s.expires_at > ?`)
    .bind(tokenHash, new Date().toISOString()).first<{ email: string; role: "user" | "owner"; provider: string; expires_at: string }>();
  if (!row) return Response.json({ authenticated: false }, { status: 401 });
  return Response.json({ authenticated: true, email: row.email, role: row.role, provider: row.provider, expiresUtc: row.expires_at });
}

async function revokeSession(request: Request, env: Env): Promise<Response> {
  const input = await request.json<SessionInput>();
  if (!input.token) return Response.json({ error: "Token não informado." }, { status: 400 });
  const tokenHash = await sha256(`session:${input.token}`);
  await env.DB.prepare("UPDATE auth_sessions SET revoked_at = ? WHERE token_hash = ?")
    .bind(new Date().toISOString(), tokenHash).run();
  return Response.json({ ok: true });
}

async function createConfirmation(request: Request, env: Env): Promise<Response> {
  const input = await request.json<ConfirmationInput>();
  if (!input.token || !input.email || !input.expiresUtc)
    return Response.json({ error: "Dados de confirmação incompletos." }, { status: 400 });
  const now = new Date().toISOString();
  const tokenHash = await sha256(`confirmation:${input.token}`);
  await env.DB.prepare(`INSERT OR REPLACE INTO confirmation_tokens
    (token_hash, email, expires_at, consumed_at, created_at)
    VALUES (?, ?, ?, NULL, ?)`)
    .bind(tokenHash, input.email.toLowerCase(), input.expiresUtc, now).run();
  return Response.json({ ok: true });
}

async function consumeConfirmation(request: Request, env: Env): Promise<Response> {
  const input = await request.json<ConfirmationInput>();
  if (!input.token)
    return Response.json({ error: "Token não informado." }, { status: 400 });
  const tokenHash = await sha256(`confirmation:${input.token}`);
  const now = new Date().toISOString();
  const update = await env.DB.prepare(`UPDATE confirmation_tokens
    SET consumed_at = ?
    WHERE token_hash = ? AND consumed_at IS NULL AND expires_at >= ?`)
    .bind(now, tokenHash, now).run();
  if ((update.meta?.changes ?? 0) !== 1)
    return Response.json({ error: "Token inválido, expirado ou já utilizado." }, { status: 400 });
  const row = await env.DB.prepare("SELECT email FROM confirmation_tokens WHERE token_hash = ?")
    .bind(tokenHash).first<{ email: string }>();
  return row ? Response.json({ email: row.email }) : Response.json({ error: "Token inválido." }, { status: 400 });
}

async function registerPassword(request: Request, env: Env): Promise<Response> {
  const input = await request.json<PasswordRegistrationInput>();
  if (!input.email || !input.hashB64 || !input.saltB64 || !input.iterations)
    return Response.json({ error: "Dados de senha incompletos." }, { status: 400 });
  const email = input.email.trim().toLowerCase();
  const existing = await env.DB.prepare(`SELECT user_id FROM password_credentials
    WHERE user_id = ?`).bind(await sha256(`user:${email}`)).first<{ user_id: string }>();
  if (existing)
    return Response.json({ error: "A conta já possui senha cadastrada." }, { status: 409 });

  const now = new Date().toISOString();
  const userId = await sha256(`user:${email}`);
  const role = isOwnerEmail(email) ? "owner" : "user";
  const plan = role === "owner" ? "owner" : "free";
  await env.DB.batch([
    env.DB.prepare(`INSERT INTO users (id, email, email_verified, role, created_at, updated_at)
      VALUES (?, ?, 1, ?, ?, ?)
      ON CONFLICT(email) DO UPDATE SET email_verified = 1,
        role = CASE WHEN users.role = 'owner' THEN 'owner' ELSE excluded.role END,
        updated_at = excluded.updated_at`)
      .bind(userId, email, role, now, now),
    env.DB.prepare(`INSERT INTO password_credentials
      (user_id, hash_b64, salt_b64, iterations, created_at, updated_at)
      VALUES (?, ?, ?, ?, ?, ?)`)
      .bind(userId, input.hashB64, input.saltB64, input.iterations, now, now),
    env.DB.prepare(`INSERT INTO entitlements
      (user_id, plan, monthly_free_queries_used, one_off_queries_available, updated_at)
      VALUES (?, ?, 0, 0, ?)
      ON CONFLICT(user_id) DO UPDATE SET
        plan = CASE WHEN entitlements.plan = 'owner' OR excluded.plan = 'owner' THEN 'owner' ELSE entitlements.plan END,
        updated_at = excluded.updated_at`)
      .bind(userId, plan, now)
  ]);
  return Response.json({ ok: true, email, role });
}

async function getPassword(request: Request, env: Env): Promise<Response> {
  const input = await request.json<PasswordLookupInput>();
  if (!input.email) return Response.json({ error: "E-mail não informado." }, { status: 400 });
  const email = input.email.trim().toLowerCase();
  const row = await env.DB.prepare(`SELECT u.email, u.role, p.hash_b64, p.salt_b64, p.iterations
    FROM password_credentials p JOIN users u ON u.id = p.user_id
    WHERE u.email = ?`).bind(email)
    .first<{ email: string; role: "user" | "owner"; hash_b64: string; salt_b64: string; iterations: number }>();
  if (!row) return Response.json({ error: "Conta ou senha inválida." }, { status: 401 });
  return Response.json({ email: row.email, role: row.role, hashB64: row.hash_b64, saltB64: row.salt_b64, iterations: row.iterations });
}

async function getEntitlements(request: Request, env: Env): Promise<Response> {
  const input = await request.json<EntitlementInput>();
  if (!input.email) return Response.json({ error: "E-mail não informado." }, { status: 400 });
  const email = input.email.trim().toLowerCase();
  const row = await env.DB.prepare(`SELECT u.id, u.role,
      COALESCE(e.plan, CASE WHEN u.role = 'owner' THEN 'owner' ELSE 'free' END) AS plan,
      COALESCE(e.monthly_free_queries_used, 0) AS monthly_free_queries_used,
      COALESCE(e.one_off_queries_available, 0) AS one_off_queries_available
    FROM users u LEFT JOIN entitlements e ON e.user_id = u.id
    WHERE u.email = ?`).bind(email)
    .first<{ id: string; role: "user" | "owner"; plan: "free" | "subscriber" | "owner"; monthly_free_queries_used: number; one_off_queries_available: number }>();
  if (!row) return Response.json({ plan: "free", monthlyFreeQueriesUsed: 0, oneOffQueriesAvailable: 0, purchases: [] });
  const purchases = await env.DB.prepare(`SELECT provider, provider_purchase_id, product_id, status, purchased_at, expires_at
    FROM purchases WHERE user_id = ? ORDER BY purchased_at DESC`).bind(row.id).all<{
      provider: string; provider_purchase_id: string; product_id: string; status: string; purchased_at: string; expires_at: string | null;
    }>();
  return Response.json({
    plan: row.plan,
    monthlyFreeQueriesUsed: row.monthly_free_queries_used,
    oneOffQueriesAvailable: row.one_off_queries_available,
    purchases: (purchases.results ?? []).map(purchase => ({
      provider: purchase.provider,
      purchaseId: purchase.provider_purchase_id,
      productId: purchase.product_id,
      status: purchase.status,
      purchasedAt: purchase.purchased_at,
      expiresAt: purchase.expires_at
    }))
  });
}

function isOwnerEmail(email: string): boolean {
  return ["leohighway@gmail.com", "leonardolimabravo@hotmail.com", "omnia051987@gmail.com"]
    .includes(email.toLowerCase());
}

async function sha256(value: string): Promise<string> {
  const bytes = new TextEncoder().encode(value);
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return [...new Uint8Array(digest)].map(byte => byte.toString(16).padStart(2, "0")).join("");
}
