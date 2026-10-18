namespace Omnia.Metadata.App;

public sealed class LoginForm : Form
{
    public string AuthenticatedEmail { get; private set; } = string.Empty;
    public string SessionToken { get; private set; } = string.Empty;

    readonly TextBox email = new() { PlaceholderText = "E-mail", Dock = DockStyle.Fill };
    readonly TextBox password = new() { PlaceholderText = "Senha", UseSystemPasswordChar = true, Dock = DockStyle.Fill };
    readonly AuthClient auth = new();

    public LoginForm()
    {
        Text = "Omnia Metadata — Login";
        Width = 520;
        Height = 570;
        MinimumSize = new(460, 520);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 9,
            Padding = new Padding(32, 26, 32, 24),
            BackColor = Color.White
        };
        layout.RowStyles.Add(new(SizeType.Absolute, 54));
        layout.RowStyles.Add(new(SizeType.Absolute, 48));
        layout.RowStyles.Add(new(SizeType.Absolute, 42));
        layout.RowStyles.Add(new(SizeType.Absolute, 42));
        layout.RowStyles.Add(new(SizeType.Absolute, 44));
        layout.RowStyles.Add(new(SizeType.Absolute, 44));
        layout.RowStyles.Add(new(SizeType.Absolute, 42));
        layout.RowStyles.Add(new(SizeType.Absolute, 42));
        layout.RowStyles.Add(new(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "OMNIA METADATA",
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(21, 45, 90)
        };
        var note = new Label
        {
            Text = "Entre com e-mail e senha ou use um provedor OAuth.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(70, 70, 70),
            AutoEllipsis = true
        };
        email.Margin = new Padding(0, 5, 0, 5);
        password.Margin = new Padding(0, 5, 0, 5);

        var emailLogin = ActionButton("Entrar com e-mail");
        emailLogin.Click += async (_, _) => await EmailLogin(emailLogin);
        var create = ActionButton("Criar conta");
        create.Click += (_, _) => { using var form = new CreateAccountForm(); form.ShowDialog(this); };

        var google = Provider("Continuar com Google");
        google.Click += async (_, _) => await ProviderLogin(google, "google", "Google");
        var github = Provider("Continuar com GitHub");
        github.Click += async (_, _) => await ProviderLogin(github, "github", "GitHub");
        var microsoft = Provider("Continuar com Microsoft");
        microsoft.Click += async (_, _) => await ProviderLogin(microsoft, "microsoft", "Microsoft");

        var providerTitle = new Label
        {
            Text = "ou entre com um provedor",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(100, 100, 100)
        };

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(note, 0, 1);
        layout.Controls.Add(email, 0, 2);
        layout.Controls.Add(password, 0, 3);
        layout.Controls.Add(emailLogin, 0, 4);
        layout.Controls.Add(create, 0, 5);
        layout.Controls.Add(providerTitle, 0, 6);
        layout.Controls.Add(google, 0, 7);

        var otherProviders = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Controls = { github, microsoft }
        };
        layout.Controls.Add(otherProviders, 0, 8);
        Controls.Add(layout);
        AcceptButton = emailLogin;
    }

    static Button ActionButton(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Height = 34,
        FlatStyle = FlatStyle.System
    };

    static Button Provider(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Height = 34,
        FlatStyle = FlatStyle.System,
        Margin = new Padding(0, 4, 6, 4)
    };

    async Task EmailLogin(Button button)
    {
        if (string.IsNullOrWhiteSpace(email.Text) || string.IsNullOrWhiteSpace(password.Text))
        {
            MessageBox.Show("Informe e-mail e senha.", "Login por e-mail", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        button.Enabled = false;
        try
        {
            var result = await auth.SignInWithPasswordAsync(email.Text.Trim(), password.Text);
            AuthenticatedEmail = result.Email;
            SessionToken = result.SessionToken;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Não foi possível entrar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed) button.Enabled = true;
        }
    }

    async Task ProviderLogin(Button button, string provider, string displayName)
    {
        button.Enabled = false;
        button.Text = $"Aguardando {displayName}...";
        try
        {
            var result = await auth.SignInWithProviderAsync(provider);
            AuthenticatedEmail = result.Email;
            SessionToken = result.SessionToken;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show("Login cancelado.", $"Login {displayName}", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Não foi possível entrar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed)
            {
                button.Enabled = true;
                button.Text = $"Continuar com {displayName}";
            }
        }
    }
}
