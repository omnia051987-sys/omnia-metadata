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

async function sha256(value: string): Promise<string> {
  const bytes = new TextEncoder().encode(value);
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return [...new Uint8Array(digest)].map(byte => byte.toString(16).padStart(2, "0")).join("");
}
