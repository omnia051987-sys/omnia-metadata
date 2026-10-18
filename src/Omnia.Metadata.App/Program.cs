using Omnia.Metadata.Core;
namespace Omnia.Metadata.App;
internal static class Program
{
    [STAThread] static void Main() { ApplicationConfiguration.Initialize(); using var login = new LoginForm(); if (login.ShowDialog() != DialogResult.OK) return; Application.Run(new MainForm(login.AuthenticatedEmail, login.Role, login.Plan, login.MonthlyFreeQueriesUsed, login.OneOffQueriesAvailable)); }
}
