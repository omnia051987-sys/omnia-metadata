using Omnia.Metadata.Core;
namespace Omnia.Metadata.App;
internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        RunApplicationAsync().GetAwaiter().GetResult();
    }

    static async Task RunApplicationAsync()
    {
        if (Environment.GetEnvironmentVariable("OMNIA_SCREENSHOT_MODE") == "1")
        {
            using var sample = new MainForm("demo@omnia-metadata.com", "owner", "owner", 0, 0);
            sample.Load += (_, _) => sample.LoadFilesForScreenshot([Environment.GetEnvironmentVariable("OMNIA_SCREENSHOT_FILE") ?? string.Empty]);
            if (Environment.GetEnvironmentVariable("OMNIA_SCREENSHOT_VIEW") == "text-ai")
                sample.Shown += (_, _) => sample.ShowTextAiForScreenshot("Este texto de exemplo apresenta contexto, descreve um procedimento e organiza informações para uma triagem técnica. A análise responsável combina sinais linguísticos, estatísticos e estruturais, sempre registrando suas limitações e evitando conclusões absolutas sobre autoria.");
            Application.Run(sample);
            return;
        }

        var sessionStore = new AuthSessionStore();
        var stored = sessionStore.TryLoad();
        AuthResult? restored = null;
        if (stored is not null)
        {
            try
            {
                restored = await new AuthClient().RestoreSessionAsync(stored);
            }
            catch
            {
                // Keep the encrypted session so a transient network error can be retried
                // on the next launch; the login dialog remains available as a fallback.
            }

            if (restored is null && stored.ExpiresUtc <= DateTime.UtcNow)
                sessionStore.Clear();
        }

        if (restored is not null)
        {
            sessionStore.Save(restored);
            Application.Run(new MainForm(
                restored.Email,
                restored.Role,
                restored.Plan,
                restored.MonthlyFreeQueriesUsed,
                restored.OneOffQueriesAvailable,
                sessionStore,
                restored.SessionToken));
            return;
        }

        using var login = new LoginForm(sessionStore);
        if (login.ShowDialog() != DialogResult.OK) return;
        Application.Run(new MainForm(
            login.AuthenticatedEmail,
            login.Role,
            login.Plan,
            login.MonthlyFreeQueriesUsed,
            login.OneOffQueriesAvailable,
            sessionStore,
            login.SessionToken));
    }
}
