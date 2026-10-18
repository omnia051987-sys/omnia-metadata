using System.Collections.Concurrent;
using System.Net.Mail;
using System.Security.Cryptography;
using MailKit.Security;
using MimeKit;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<TokenStore>();
builder.Services.AddSingleton<SmtpConfirmationSender>();
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { service = "omnia-auth", status = "ok", utc = DateTime.UtcNow }));
app.MapPost("/auth/request-confirmation", async (ConfirmationRequest req, TokenStore store, SmtpConfirmationSender mailer) =>
{
    try { _ = new MailAddress(req.Email); } catch { return Results.BadRequest(new { error = "E-mail inválido." }); }
    var token = store.Create(req.Email);
    var baseUrl = Environment.GetEnvironmentVariable("OMNIA_AUTH_BASE_URL") ?? "https://auth.omniametadata.com";
    var link = $"{baseUrl}/auth/confirm?token={Uri.EscapeDataString(token)}";
    try { await mailer.SendAsync(req.Email.Trim(), link).WaitAsync(TimeSpan.FromSeconds(15)); }
    catch (TimeoutException) { return Results.Problem("Timeout na conexão SMTP.", statusCode: 503); }
    catch (Exception ex) { return Results.Problem($"Falha SMTP: {ex.GetType().Name}", statusCode: 502); }
    return Results.Accepted(value: new { message = "Solicitação registrada. Verifique seu e-mail.", sender = mailer.From });
});
app.MapGet("/auth/confirm", (string token, TokenStore store) => store.Consume(token) is string email ? Results.Ok(new { confirmed = true, email }) : Results.BadRequest(new { confirmed = false, error = "Token inválido, expirado ou já utilizado." }));
app.MapPost("/auth/revoke", (RevokeRequest req, TokenStore store) => { store.Revoke(req.SessionId); return Results.NoContent(); });
app.MapGet("/auth/{provider}", (string provider) => Results.StatusCode(501));
app.Run();

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
        message.Body = new TextPart("plain") { Text = $"Olá,\n\nConfirme sua conta pelo link abaixo:\n\n{link}\n\nEste link expira em 24 horas.\n\nOmnia Metadata" };
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
    readonly ConcurrentDictionary<string, DateTime> revoked = new();
    public string Create(string email) { var bytes = RandomNumberGenerator.GetBytes(32); var token = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('='); pending[token] = (email.Trim(), DateTime.UtcNow.AddHours(24)); return token; }
    public string? Consume(string token) { if (!pending.TryRemove(token, out var x) || x.Expires < DateTime.UtcNow) return null; return x.Email; }
    public void Revoke(string session) => revoked[session] = DateTime.UtcNow;
}
