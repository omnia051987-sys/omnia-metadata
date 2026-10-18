using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;

namespace Omnia.Metadata.App;

public sealed record AuthResult(
    string Email,
    string Role,
    string SessionToken,
    DateTime ExpiresUtc,
    string Provider,
    string Plan = "free",
    int MonthlyFreeQueriesUsed = 0,
    int OneOffQueriesAvailable = 0,
    PurchaseSummary[]? Purchases = null);

public sealed record PurchaseSummary(
    string Provider,
    string PurchaseId,
    string ProductId,
    string Status,
    string PurchasedAt,
    string? ExpiresAt);

public sealed class AuthClient
{
    public static string BaseUrl =>
        (Environment.GetEnvironmentVariable("OMNIA_AUTH_BASE_URL") ?? "https://omnia-metadata-auth-api.onrender.com").TrimEnd('/');

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task RegisterAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        using var response = await Http.PostAsJsonAsync(
            $"{BaseUrl}/auth/register",
            new { email, password },
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadErrorAsync(response, "O servidor recusou o cadastro."));
    }

    public async Task<AuthResult> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        using var response = await Http.PostAsJsonAsync(
            $"{BaseUrl}/auth/password/login",
            new { email, password },
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadErrorAsync(response, "E-mail ou senha inválidos."));
        var result = await response.Content.ReadFromJsonAsync<AuthResult>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
        return result ?? throw new InvalidOperationException("Resposta de autenticação vazia.");
    }

    public Task<AuthResult> SignInWithGoogleAsync(CancellationToken cancellationToken = default) =>
        SignInWithProviderAsync("google", cancellationToken);

    public async Task<AuthResult> SignInWithProviderAsync(string provider, CancellationToken cancellationToken = default)
    {
        var port = GetFreePort();
        var returnUri = $"http://127.0.0.1:{port}/auth/callback/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(returnUri);
        listener.Start();

        var authorizeUrl = $"{BaseUrl}/auth/{Uri.EscapeDataString(provider)}?return_uri={Uri.EscapeDataString(returnUri)}";
        Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true });

        using var registration = cancellationToken.Register(() => listener.Stop());
        HttpListenerContext context;
        try
        {
            context = await listener.GetContextAsync();
        }
        catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Login cancelado.", cancellationToken);
        }

        var ticket = context.Request.QueryString["ticket"];
        var error = context.Request.QueryString["error"];
        await CompleteBrowserRequestAsync(context, string.IsNullOrWhiteSpace(ticket)
            ? "O login não foi concluído. Você pode fechar esta janela."
            : "Login concluído. Você pode fechar esta janela e voltar ao Omnia Metadata.");

        if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(ticket))
            throw new InvalidOperationException("O provedor não concluiu a autenticação.");

        using var response = await Http.PostAsJsonAsync(
            $"{BaseUrl}/auth/desktop/exchange",
            new { ticket },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"O servidor recusou a sessão do aplicativo ({(int)response.StatusCode}).");

        var result = await response.Content.ReadFromJsonAsync<AuthResult>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
        return result ?? throw new InvalidOperationException("Resposta de autenticação vazia.");
    }

    static async Task CompleteBrowserRequestAsync(HttpListenerContext context, string message)
    {
        const string page = "<!doctype html><meta charset='utf-8'><title>Omnia Metadata</title>" +
            "<body style='font-family:Segoe UI,sans-serif;padding:32px;background:#f7f9fc;color:#152d5a'>" +
            "<h2>OMNIA METADATA</h2><p>{0}</p><p>Esta janela pode ser fechada.</p></body>";
        var bytes = System.Text.Encoding.UTF8.GetBytes(string.Format(page, WebUtility.HtmlEncode(message)));
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    static int GetFreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    static async Task<string> ReadErrorAsync(HttpResponseMessage response, string fallback)
    {
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (json.RootElement.TryGetProperty("error", out var error)) return error.GetString() ?? fallback;
            if (json.RootElement.TryGetProperty("detail", out var detail)) return detail.GetString() ?? fallback;
        }
        catch (JsonException) { }
        return $"{fallback} ({(int)response.StatusCode}).";
    }
}
