using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MailKit.Security;
using MimeKit;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();
builder.Services.AddSingleton<TokenStore>();
builder.Services.AddSingleton<SmtpConfirmationSender>();
builder.Services.AddSingleton<AuthSessionStore>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { service = "omnia-auth", status = "ok", utc = DateTime.UtcNow }));

app.MapPost("/auth/request-confirmation", async (ConfirmationRequest req, TokenStore store, SmtpConfirmationSender mailer) =>
{
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
});

app.MapGet("/auth/confirm", (string token, TokenStore store) =>
    store.Consume(token) is string email
        ? Results.Ok(new { confirmed = true, email })
        : Results.BadRequest(new { confirmed = false, error = "Token inválido, expirado ou já utilizado." }));

app.MapGet("/auth/google", (HttpContext http) =>
{
    var clientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
    if (string.IsNullOrWhiteSpace(clientId))
        return Results.Problem("GOOGLE_CLIENT_ID não configurado.", statusCode: 503);

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
});

app.MapGet("/auth/google/callback", async (
    HttpContext http,
    IHttpClientFactory clients,
    AuthSessionStore sessions,
    string? code,
    string? state,
    string? error) =>
{
    if (!string.IsNullOrWhiteSpace(error))
        return Results.BadRequest(new { authenticated = false, error = "Autorização Google cancelada." });

    // Read the one-time state before expiring the cookie. Deleting it first
    // makes every valid callback look like a missing-state replay.
    var expectedState = http.Request.Cookies["omnia_oauth_state"];
    http.Response.Cookies.Delete("omnia_oauth_state");
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
        http.Response.Cookies.Append("omnia_session", session.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromHours(8),
            IsEssential = true
        });

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
});

app.MapGet("/auth/me", (HttpContext http, AuthSessionStore sessions) =>
{
    if (!http.Request.Cookies.TryGetValue("omnia_session", out var token) || !sessions.TryGet(token, out var session))
        return Results.Unauthorized();

    return Results.Ok(new
    {
        authenticated = true,
        session.Email,
        session.Role,
        session.ExpiresUtc
    });
});

app.MapPost("/auth/logout", (HttpContext http, AuthSessionStore sessions) =>
{
    if (http.Request.Cookies.TryGetValue("omnia_session", out var token))
        sessions.Revoke(token);
    http.Response.Cookies.Delete("omnia_session");
    return Results.NoContent();
});

app.MapPost("/auth/revoke", (RevokeRequest req, AuthSessionStore sessions) =>
{
    sessions.Revoke(req.SessionId);
    return Results.NoContent();
});

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

record ConfirmationRequest(string Email);
record RevokeRequest(string SessionId);

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
