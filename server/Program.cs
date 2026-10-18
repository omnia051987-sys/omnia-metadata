using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using MailKit.Security;
using MimeKit;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<D1SessionStore>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.AddSingleton<TokenStore>();
builder.Services.AddSingleton<SmtpConfirmationSender>();
builder.Services.AddSingleton<AuthSessionStore>();
builder.Services.AddSingleton<DesktopTicketStore>();

var app = builder.Build();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    var isAuthRequest = context.Request.Path.StartsWithSegments("/auth");
    if (isAuthRequest)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
    }

    await next();
});

app.MapGet("/health", () => Results.Ok(new { service = "omnia-auth", status = "ok", utc = DateTime.UtcNow }));

app.MapPost("/auth/request-confirmation", async (ConfirmationRequest? req, TokenStore store, SmtpConfirmationSender mailer) =>
{
    if (req is null || string.IsNullOrWhiteSpace(req.Email) || req.Email.Length > 320)
        return Results.BadRequest(new { error = "Informe um e-mail válido." });

    try { _ = new MailAddress(req.Email); }
    catch { return Results.BadRequest(new { error = "E-mail inválido." }); }

    var token = store.Create(req.Email);
    var baseUrl = Environment.GetEnvironmentVariable("OMNIA_AUTH_BASE_URL") ?? "https://auth.omniametadata.com";
    var link = $"{baseUrl.TrimEnd('/')}/auth/confirm?token={Uri.EscapeDataString(token)}";

    try
    {
        await mailer.SendAsync(req.Email.Trim(), link).WaitAsync(TimeSpan.FromSeconds(15));
    }
    catch (TimeoutException)
    {
        return Results.Problem("Timeout na conexão SMTP.", statusCode: 503);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Falha SMTP: {ex.GetType().Name}", statusCode: 502);
    }

    return Results.Accepted(value: new
    {
        message = "Solicitação registrada. Verifique seu e-mail.",
        sender = mailer.From
    });
}).RequireRateLimiting("auth");

app.MapGet("/auth/confirm", (string? token, TokenStore store) =>
    string.IsNullOrWhiteSpace(token)
        ? Results.BadRequest(new { confirmed = false, error = "Token não informado." })
        : store.Consume(token) is string email
            ? Results.Ok(new { confirmed = true, email })
            : Results.BadRequest(new { confirmed = false, error = "Token inválido, expirado ou já utilizado." }))
    .RequireRateLimiting("auth");

app.MapGet("/auth/google", (HttpContext http, string? return_uri) =>
{
    var clientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
    if (string.IsNullOrWhiteSpace(clientId))
        return Results.Problem("GOOGLE_CLIENT_ID não configurado.", statusCode: 503);

    if (!string.IsNullOrWhiteSpace(return_uri) && !IsSafeDesktopReturnUri(return_uri))
        return Results.BadRequest(new { authenticated = false, error = "Endereço de retorno local inválido." });

    // The state is carried in a secure, short-lived cookie so a Render cold start
    // between authorization and callback does not invalidate a valid login.
    var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    http.Response.Cookies.Append("omnia_oauth_state", state, new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.None,
        Path = "/",
        MaxAge = TimeSpan.FromMinutes(10),
        IsEssential = true
    });
    if (!string.IsNullOrWhiteSpace(return_uri))
    {
        http.Response.Cookies.Append("omnia_oauth_return", return_uri, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = "/",
            MaxAge = TimeSpan.FromMinutes(10),
            IsEssential = true
        });
    }

    var redirectUri = GetGoogleRedirectUri();
    var query = string.Join("&", new Dictionary<string, string>
    {
        ["client_id"] = clientId,
        ["redirect_uri"] = redirectUri,
        ["response_type"] = "code",
        ["scope"] = "openid email profile",
        ["access_type"] = "online",
        ["include_granted_scopes"] = "true",
        ["state"] = state,
        ["prompt"] = "select_account"
    }.Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));

    return Results.Redirect($"https://accounts.google.com/o/oauth2/v2/auth?{query}");
}).RequireRateLimiting("auth");

app.MapGet("/auth/google/callback", async (
    HttpContext http,
    IHttpClientFactory clients,
    AuthSessionStore sessions,
    DesktopTicketStore desktopTickets,
    D1SessionStore durable,
    string? code,
    string? state,
    string? error) =>
{
    if (!string.IsNullOrWhiteSpace(error))
        return Results.BadRequest(new { authenticated = false, error = "Autorização Google cancelada." });

    // Read the one-time state before expiring the cookie. Deleting it first
    // makes every valid callback look like a missing-state replay.
    var expectedState = http.Request.Cookies["omnia_oauth_state"];
    var desktopReturnUri = http.Request.Cookies["omnia_oauth_return"];
    http.Response.Cookies.Delete("omnia_oauth_state");
    http.Response.Cookies.Delete("omnia_oauth_return");
    var stateMatches = !string.IsNullOrWhiteSpace(state)
        && !string.IsNullOrWhiteSpace(expectedState)
        && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(state),
            Encoding.UTF8.GetBytes(expectedState));

    app.Logger.LogInformation(
        "OAuth Google state validation: queryPresent={QueryPresent}, cookiePresent={CookiePresent}, match={Match}",
        !string.IsNullOrWhiteSpace(state),
        !string.IsNullOrWhiteSpace(expectedState),
        stateMatches);

    if (string.IsNullOrWhiteSpace(code) || !stateMatches)
        return Results.BadRequest(new { authenticated = false, error = "Estado OAuth inválido, expirado ou já utilizado." });

    var clientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
    var clientSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET");
    if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        return Results.Problem("Credenciais Google não configuradas.", statusCode: 503);

    try
    {
        var client = clients.CreateClient();
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = GetGoogleRedirectUri(),
                ["grant_type"] = "authorization_code"
            })
        };

        using var tokenResponse = await client.SendAsync(tokenRequest);
        if (!tokenResponse.IsSuccessStatusCode)
            return Results.Problem("O Google recusou a troca do código de autorização.", statusCode: 502);

        using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        if (!tokenJson.RootElement.TryGetProperty("access_token", out var accessTokenElement))
            return Results.Problem("Resposta OAuth do Google sem access_token.", statusCode: 502);

        using var userRequest = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
        userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessTokenElement.GetString());
        using var userResponse = await client.SendAsync(userRequest);
        if (!userResponse.IsSuccessStatusCode)
            return Results.Problem("Não foi possível validar o perfil no Google.", statusCode: 502);

        using var userJson = JsonDocument.Parse(await userResponse.Content.ReadAsStringAsync());
        var root = userJson.RootElement;
        var email = root.GetProperty("email").GetString()?.Trim().ToLowerInvariant();
        var subject = root.GetProperty("sub").GetString();
        var emailVerified = root.TryGetProperty("email_verified", out var verified) && verified.GetBoolean();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(subject) || !emailVerified)
            return Results.BadRequest(new { authenticated = false, error = "O Google não confirmou o e-mail desta conta." });

        var role = IsOwnerEmail(email) ? "owner" : "user";
        var session = sessions.Create(email, subject, role);
        if (!await durable.CreateAsync(session, "google"))
            return Results.Problem("Não foi possível persistir a sessão com segurança.", statusCode: 503);
        http.Response.Cookies.Append("omnia_session", session.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromHours(8),
            IsEssential = true
        });

        if (IsSafeDesktopReturnUri(desktopReturnUri))
        {
            var ticket = desktopTickets.Create(session.Token);
            return Results.Redirect($"{desktopReturnUri}?ticket={Uri.EscapeDataString(ticket)}");
        }

        return Results.Ok(new
        {
            authenticated = true,
            provider = "google",
            email,
            role,
            expiresUtc = session.ExpiresUtc
        });
    }
    catch (JsonException)
    {
        return Results.Problem("Resposta inválida recebida do Google.", statusCode: 502);
    }
    catch (HttpRequestException)
    {
        return Results.Problem("Falha de comunicação com o Google.", statusCode: 502);
    }
}).RequireRateLimiting("auth");

app.MapPost("/auth/desktop/exchange", (DesktopTicketRequest? req, DesktopTicketStore desktopTickets, AuthSessionStore sessions) =>
{
    if (req is null || string.IsNullOrWhiteSpace(req.Ticket))
        return Results.BadRequest(new { authenticated = false, error = "Ticket não informado." });

    if (!desktopTickets.Consume(req.Ticket, out var sessionToken) || !sessions.TryGet(sessionToken, out var session))
        return Results.Unauthorized();

    return Results.Ok(new
    {
        authenticated = true,
        provider = "google",
        sessionToken,
        session.Email,
        session.Role,
        session.ExpiresUtc
    });
}).RequireRateLimiting("auth");

app.MapGet("/auth/me", async (HttpContext http, AuthSessionStore sessions, D1SessionStore durable) =>
{
    if (!http.Request.Cookies.TryGetValue("omnia_session", out var token))
        return Results.Unauthorized();

    if (!sessions.TryGet(token, out var session))
    {
        session = await durable.GetAsync(token);
        if (session is null) return Results.Unauthorized();
    }

    return Results.Ok(new
    {
        authenticated = true,
        session.Email,
        session.Role,
        session.ExpiresUtc
    });
}).RequireRateLimiting("auth");

app.MapPost("/auth/logout", async (HttpContext http, AuthSessionStore sessions, D1SessionStore durable) =>
{
    if (http.Request.Cookies.TryGetValue("omnia_session", out var token))
    {
        sessions.Revoke(token);
        await durable.RevokeAsync(token);
    }
    http.Response.Cookies.Delete("omnia_session");
    return Results.NoContent();
}).RequireRateLimiting("auth");

app.MapPost("/auth/revoke", async (HttpContext http, AuthSessionStore sessions, D1SessionStore durable) =>
{
    if (!http.Request.Cookies.TryGetValue("omnia_session", out var token) || string.IsNullOrWhiteSpace(token))
        return Results.Unauthorized();

    if (!sessions.TryGet(token, out _))
    {
        var remote = await durable.GetAsync(token);
        if (remote is null) return Results.Unauthorized();
    }

    sessions.Revoke(token);
    await durable.RevokeAsync(token);
    http.Response.Cookies.Delete("omnia_session");
    return Results.NoContent();
}).RequireRateLimiting("auth");

app.MapGet("/auth/{provider}", (string provider) => Results.StatusCode(501));
app.Run();

static bool IsOwnerEmail(string email) => email.Equals("leohighway@gmail.com", StringComparison.OrdinalIgnoreCase)
    || email.Equals("leonardolimabravo@hotmail.com", StringComparison.OrdinalIgnoreCase)
    || email.Equals("omnia051987@gmail.com", StringComparison.OrdinalIgnoreCase);

static string GetGoogleRedirectUri()
{
    var baseUrl = Environment.GetEnvironmentVariable("OMNIA_AUTH_BASE_URL") ?? "https://auth.omniametadata.com";
    return $"{baseUrl.TrimEnd('/')}/auth/google/callback";
}

static bool IsSafeDesktopReturnUri(string? value)
{
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
    if (uri.Scheme != Uri.UriSchemeHttp) return false;
    if (uri.Host is not ("127.0.0.1" or "localhost")) return false;
    return uri.Port is >= 1024 and <= 65535
        && (uri.AbsolutePath == "/auth/callback" || uri.AbsolutePath == "/auth/callback/");
}

record ConfirmationRequest(string Email);
record DesktopTicketRequest(string Ticket);

sealed class SmtpConfirmationSender
{
    public string From => Environment.GetEnvironmentVariable("SMTP_FROM") ?? Environment.GetEnvironmentVariable("SMTP_USER") ?? "omnia051987@gmail.com";

    public async Task SendAsync(string recipient, string link)
    {
        var host = Environment.GetEnvironmentVariable("SMTP_HOST") ?? "smtp.gmail.com";
        var port = int.TryParse(Environment.GetEnvironmentVariable("SMTP_PORT"), out var p) ? p : 465;
        var user = Environment.GetEnvironmentVariable("SMTP_USER") ?? From;
        var password = Environment.GetEnvironmentVariable("SMTP_PASSWORD");
        if (string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException("SMTP_PASSWORD não configurado.");

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(From));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = "Confirme seu e-mail — Omnia Metadata";
        message.Body = new TextPart("plain")
        {
            Text = $"Olá,\n\nConfirme sua conta pelo link abaixo:\n\n{link}\n\nEste link expira em 24 horas.\n\nOmnia Metadata"
        };

        using var client = new MailKit.Net.Smtp.SmtpClient();
        await client.ConnectAsync(host, port, port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls);
        await client.AuthenticateAsync(user, password);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }
}

sealed class TokenStore
{
    readonly ConcurrentDictionary<string, (string Email, DateTime Expires)> pending = new();

    public string Create(string email)
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        pending[token] = (email.Trim(), DateTime.UtcNow.AddHours(24));
        return token;
    }

    public string? Consume(string token) =>
        pending.TryRemove(token, out var value) && value.Expires >= DateTime.UtcNow ? value.Email : null;
}

sealed record AuthSession(string Token, string Email, string Subject, string Role, DateTime ExpiresUtc);

sealed class AuthSessionStore
{
    readonly ConcurrentDictionary<string, AuthSession> sessions = new();

    public AuthSession Create(string email, string subject, string role)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var session = new AuthSession(token, email, subject, role, DateTime.UtcNow.AddHours(8));
        sessions[token] = session;
        return session;
    }

    public bool TryGet(string token, out AuthSession session)
    {
        if (sessions.TryGetValue(token, out session!) && session.ExpiresUtc >= DateTime.UtcNow)
            return true;

        if (session is not null)
            sessions.TryRemove(token, out _);
        session = null!;
        return false;
    }

    public void Revoke(string token) => sessions.TryRemove(token, out _);
}

sealed class DesktopTicketStore
{
    readonly ConcurrentDictionary<string, (string SessionToken, DateTime ExpiresUtc)> tickets = new();

    public string Create(string sessionToken)
    {
        var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        tickets[ticket] = (sessionToken, DateTime.UtcNow.AddMinutes(2));
        return ticket;
    }

    public bool Consume(string ticket, out string sessionToken)
    {
        if (tickets.TryRemove(ticket, out var value) && value.ExpiresUtc >= DateTime.UtcNow)
        {
            sessionToken = value.SessionToken;
            return true;
        }

        sessionToken = string.Empty;
        return false;
    }
}

sealed class D1SessionStore
{
    readonly HttpClient client;
    readonly string? baseUrl = Environment.GetEnvironmentVariable("OMNIA_D1_STORE_URL")?.TrimEnd('/');
    readonly string? secret = Environment.GetEnvironmentVariable("OMNIA_D1_STORE_SECRET");

    public D1SessionStore(HttpClient client) => this.client = client;
    bool Enabled => !string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(secret);

    public async Task<bool> CreateAsync(AuthSession session, string provider)
    {
        if (!Enabled) return true;
        using var request = NewRequest("/internal/session/create", new
        {
            token = session.Token,
            email = session.Email,
            subject = session.Subject,
            provider,
            role = session.Role,
            expiresUtc = session.ExpiresUtc
        });
        try { using var response = await client.SendAsync(request); return response.IsSuccessStatusCode; }
        catch (HttpRequestException) { return false; }
    }

    public async Task<AuthSession?> GetAsync(string token)
    {
        if (!Enabled) return null;
        using var request = NewRequest("/internal/session/get", new { token });
        try
        {
            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            if (!root.TryGetProperty("authenticated", out var authenticated) || !authenticated.GetBoolean()) return null;
            return new AuthSession(
                token,
                root.GetProperty("email").GetString()!,
                string.Empty,
                root.GetProperty("role").GetString()!,
                root.GetProperty("expiresUtc").GetDateTime());
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or FormatException) { return null; }
    }

    public async Task RevokeAsync(string token)
    {
        if (!Enabled) return;
        using var request = NewRequest("/internal/session/revoke", new { token });
        try { using var _ = await client.SendAsync(request); }
        catch (HttpRequestException) { }
    }

    HttpRequestMessage NewRequest(string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return request;
    }
}
