using Omnia.Metadata.Core;
namespace Omnia.Metadata.App;
internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        if (Environment.GetEnvironmentVariable("OMNIA_SCREENSHOT_MODE") == "1")
        {
            using var sample = new MainForm("demo@omnia-metadata.com", "owner", "owner", 0, 0);
            sample.Load += (_, _) => sample.LoadFilesForScreenshot([Environment.GetEnvironmentVariable("OMNIA_SCREENSHOT_FILE") ?? string.Empty]);
            if (Environment.GetEnvironmentVariable("OMNIA_SCREENSHOT_VIEW") == "text-ai")
                sample.Shown += (_, _) => sample.ShowTextAiForScreenshot("Este texto de exemplo apresenta contexto, descreve um procedimento e organiza informações para uma triagem técnica. A análise responsável combina sinais linguísticos, estatísticos e estruturais, sempre registrando suas limitações e evitando conclusões absolutas sobre autoria.");
            Application.Run(sample);
            return;
        }

        using var login = new LoginForm();
        if (login.ShowDialog() != DialogResult.OK) return;
        Application.Run(new MainForm(login.AuthenticatedEmail, login.Role, login.Plan, login.MonthlyFreeQueriesUsed, login.OneOffQueriesAvailable));
    }
}
