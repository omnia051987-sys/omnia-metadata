using System.Net.Http.Json;

namespace Omnia.Metadata.App;

public sealed class CreateAccountForm : Form
{
    readonly TextBox email = new() { PlaceholderText = "E-mail", Dock = DockStyle.Fill };
    readonly Button create = new() { Text = "Enviar link de confirmação", Dock = DockStyle.Fill };
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public CreateAccountForm()
    {
        Text = "Omnia Metadata — Criar conta";
        Width = 500;
        Height = 300;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28),
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Color.White
        };
        grid.RowStyles.Add(new(SizeType.Absolute, 44));
        grid.RowStyles.Add(new(SizeType.Absolute, 48));
        grid.RowStyles.Add(new(SizeType.Absolute, 64));
        grid.RowStyles.Add(new(SizeType.Absolute, 40));
        grid.RowStyles.Add(new(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "Criar conta",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(21, 45, 90)
        };
        var info = new Label
        {
            Text = "Informe seu e-mail. Enviaremos um link de confirmação pela conta oficial do Omnia Metadata.",
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            ForeColor = Color.FromArgb(70, 70, 70)
        };
        email.Margin = new Padding(0, 6, 0, 6);
        create.Click += async (_, _) => await RequestConfirmation();

        grid.Controls.Add(title, 0, 0);
        grid.Controls.Add(info, 0, 1);
        grid.Controls.Add(email, 0, 2);
        grid.Controls.Add(create, 0, 3);
        Controls.Add(grid);
        AcceptButton = create;
    }

    async Task RequestConfirmation()
    {
        var address = email.Text.Trim();
        if (string.IsNullOrWhiteSpace(address) || !address.Contains('@'))
        {
            MessageBox.Show("Informe um e-mail válido.", "Criar conta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        create.Enabled = false;
        try
        {
            using var response = await http.PostAsJsonAsync(
                $"{AuthClient.BaseUrl}/auth/request-confirmation",
                new { email = address });
            if (!response.IsSuccessStatusCode)
            {
                MessageBox.Show($"O servidor não aceitou a solicitação ({(int)response.StatusCode}).", "Criar conta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show(
                "Solicitação enviada. Verifique a caixa de entrada e o spam para confirmar o e-mail.",
                "Confirmação enviada",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível solicitar a confirmação: {ex.Message}", "Criar conta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed) create.Enabled = true;
        }
    }
}
