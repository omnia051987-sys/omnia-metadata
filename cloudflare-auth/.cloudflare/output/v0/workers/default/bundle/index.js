// src/index.ts
var index_default = { async fetch(request, env) {
  const url = new URL(request.url);
  if (url.pathname === "/health") return Response.json({ service: "omnia-auth", status: "ok", environment: "staging" });
  if (url.pathname === "/auth/request-confirmation" && request.method === "POST") {
    const body = await request.json();
    if (!body.email || !body.email.includes("@")) return Response.json({ error: "E-mail inv\xE1lido." }, { status: 400 });
    return Response.json({ message: "Solicita\xE7\xE3o recebida; SMTP ainda n\xE3o configurado." }, { status: 202 });
  }
  return Response.json({ service: "omnia-auth", status: "not_found" }, { status: 404 });
} };
export {
  index_default as default
};
//# sourceMappingURL=index.js.map
