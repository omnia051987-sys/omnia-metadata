var __defProp = Object.defineProperty;
var __name = (target, value) => __defProp(target, "name", { value, configurable: true });

// src/index.ts
var index_default = {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname === "/health")
      return Response.json({ service: "omnia-auth-store", status: "ok", persistent: true });
    if (!env.INTERNAL_API_SECRET || request.headers.get("Authorization") !== `Bearer ${env.INTERNAL_API_SECRET}`)
      return Response.json({ error: "N\xE3o autorizado." }, { status: 401 });
    if (request.method !== "POST")
      return Response.json({ error: "M\xE9todo n\xE3o permitido." }, { status: 405 });
    try {
      if (url.pathname === "/internal/session/create") return createSession(request, env);
      if (url.pathname === "/internal/session/get") return getSession(request, env);
      if (url.pathname === "/internal/session/revoke") return revokeSession(request, env);
      if (url.pathname === "/internal/confirmation/create") return createConfirmation(request, env);
      if (url.pathname === "/internal/confirmation/consume") return consumeConfirmation(request, env);
      if (url.pathname === "/internal/password/register") return registerPassword(request, env);
      if (url.pathname === "/internal/password/get") return getPassword(request, env);
      return Response.json({ error: "N\xE3o encontrado." }, { status: 404 });
    } catch (error) {
      console.error("D1 auth store error", error);
      return Response.json({ error: "Falha na persist\xEAncia." }, { status: 500 });
    }
  }
};
async function createSession(request, env) {
  const input = await request.json();
  if (!input.token || !input.email || !input.subject || !input.provider || !input.role || !input.expiresUtc)
    return Response.json({ error: "Dados de sess\xE3o incompletos." }, { status: 400 });
  const now = (/* @__PURE__ */ new Date()).toISOString();
  const userId = await sha256(`user:${input.email.toLowerCase()}`);
  const tokenHash = await sha256(`session:${input.token}`);
  await env.DB.batch([
    env.DB.prepare(`INSERT INTO users (id, email, email_verified, role, created_at, updated_at)
      VALUES (?, ?, 1, ?, ?, ?)
      ON CONFLICT(email) DO UPDATE SET email_verified = 1, role = CASE WHEN users.role = 'owner' THEN 'owner' ELSE excluded.role END, updated_at = excluded.updated_at`).bind(userId, input.email.toLowerCase(), input.role, now, now),
    env.DB.prepare(`INSERT INTO identities (provider, provider_subject, user_id, created_at)
      VALUES (?, ?, ?, ?)
      ON CONFLICT(provider, provider_subject) DO UPDATE SET user_id = excluded.user_id`).bind(input.provider, input.subject, userId, now),
    env.DB.prepare(`INSERT OR REPLACE INTO auth_sessions (token_hash, user_id, provider, expires_at, revoked_at, created_at)
      VALUES (?, ?, ?, ?, NULL, ?)`).bind(tokenHash, userId, input.provider, input.expiresUtc, now)
  ]);
  return Response.json({ ok: true });
}
__name(createSession, "createSession");
async function getSession(request, env) {
  const input = await request.json();
  if (!input.token) return Response.json({ error: "Token n\xE3o informado." }, { status: 400 });
  const tokenHash = await sha256(`session:${input.token}`);
  const row = await env.DB.prepare(`SELECT u.email, u.role, s.provider, s.expires_at
    FROM auth_sessions s JOIN users u ON u.id = s.user_id
    WHERE s.token_hash = ? AND s.revoked_at IS NULL AND s.expires_at > ?`).bind(tokenHash, (/* @__PURE__ */ new Date()).toISOString()).first();
  if (!row) return Response.json({ authenticated: false }, { status: 401 });
  return Response.json({ authenticated: true, email: row.email, role: row.role, provider: row.provider, expiresUtc: row.expires_at });
}
__name(getSession, "getSession");
async function revokeSession(request, env) {
  const input = await request.json();
  if (!input.token) return Response.json({ error: "Token n\xE3o informado." }, { status: 400 });
  const tokenHash = await sha256(`session:${input.token}`);
  await env.DB.prepare("UPDATE auth_sessions SET revoked_at = ? WHERE token_hash = ?").bind((/* @__PURE__ */ new Date()).toISOString(), tokenHash).run();
  return Response.json({ ok: true });
}
__name(revokeSession, "revokeSession");
async function createConfirmation(request, env) {
  const input = await request.json();
  if (!input.token || !input.email || !input.expiresUtc)
    return Response.json({ error: "Dados de confirma\xE7\xE3o incompletos." }, { status: 400 });
  const now = (/* @__PURE__ */ new Date()).toISOString();
  const tokenHash = await sha256(`confirmation:${input.token}`);
  await env.DB.prepare(`INSERT OR REPLACE INTO confirmation_tokens
    (token_hash, email, expires_at, consumed_at, created_at)
    VALUES (?, ?, ?, NULL, ?)`).bind(tokenHash, input.email.toLowerCase(), input.expiresUtc, now).run();
  return Response.json({ ok: true });
}
__name(createConfirmation, "createConfirmation");
async function consumeConfirmation(request, env) {
  const input = await request.json();
  if (!input.token)
    return Response.json({ error: "Token n\xE3o informado." }, { status: 400 });
  const tokenHash = await sha256(`confirmation:${input.token}`);
  const now = (/* @__PURE__ */ new Date()).toISOString();
  const update = await env.DB.prepare(`UPDATE confirmation_tokens
    SET consumed_at = ?
    WHERE token_hash = ? AND consumed_at IS NULL AND expires_at >= ?`).bind(now, tokenHash, now).run();
  if ((update.meta?.changes ?? 0) !== 1)
    return Response.json({ error: "Token inv\xE1lido, expirado ou j\xE1 utilizado." }, { status: 400 });
  const row = await env.DB.prepare("SELECT email FROM confirmation_tokens WHERE token_hash = ?").bind(tokenHash).first();
  return row ? Response.json({ email: row.email }) : Response.json({ error: "Token inv\xE1lido." }, { status: 400 });
}
__name(consumeConfirmation, "consumeConfirmation");
async function registerPassword(request, env) {
  const input = await request.json();
  if (!input.email || !input.hashB64 || !input.saltB64 || !input.iterations)
    return Response.json({ error: "Dados de senha incompletos." }, { status: 400 });
  const email = input.email.trim().toLowerCase();
  const existing = await env.DB.prepare(`SELECT user_id FROM password_credentials
    WHERE user_id = ?`).bind(await sha256(`user:${email}`)).first();
  if (existing)
    return Response.json({ error: "A conta j\xE1 possui senha cadastrada." }, { status: 409 });
  const now = (/* @__PURE__ */ new Date()).toISOString();
  const userId = await sha256(`user:${email}`);
  const role = isOwnerEmail(email) ? "owner" : "user";
  await env.DB.batch([
    env.DB.prepare(`INSERT INTO users (id, email, email_verified, role, created_at, updated_at)
      VALUES (?, ?, 1, ?, ?, ?)
      ON CONFLICT(email) DO UPDATE SET email_verified = 1,
        role = CASE WHEN users.role = 'owner' THEN 'owner' ELSE excluded.role END,
        updated_at = excluded.updated_at`).bind(userId, email, role, now, now),
    env.DB.prepare(`INSERT INTO password_credentials
      (user_id, hash_b64, salt_b64, iterations, created_at, updated_at)
      VALUES (?, ?, ?, ?, ?, ?)`).bind(userId, input.hashB64, input.saltB64, input.iterations, now, now)
  ]);
  return Response.json({ ok: true, email, role });
}
__name(registerPassword, "registerPassword");
async function getPassword(request, env) {
  const input = await request.json();
  if (!input.email) return Response.json({ error: "E-mail n\xE3o informado." }, { status: 400 });
  const email = input.email.trim().toLowerCase();
  const row = await env.DB.prepare(`SELECT u.email, u.role, p.hash_b64, p.salt_b64, p.iterations
    FROM password_credentials p JOIN users u ON u.id = p.user_id
    WHERE u.email = ?`).bind(email).first();
  if (!row) return Response.json({ error: "Conta ou senha inv\xE1lida." }, { status: 401 });
  return Response.json({ email: row.email, role: row.role, hashB64: row.hash_b64, saltB64: row.salt_b64, iterations: row.iterations });
}
__name(getPassword, "getPassword");
function isOwnerEmail(email) {
  return ["leohighway@gmail.com", "leonardolimabravo@hotmail.com", "omnia051987@gmail.com"].includes(email.toLowerCase());
}
__name(isOwnerEmail, "isOwnerEmail");
async function sha256(value) {
  const bytes = new TextEncoder().encode(value);
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, "0")).join("");
}
__name(sha256, "sha256");
export {
  index_default as default
};
//# sourceMappingURL=index.js.map
