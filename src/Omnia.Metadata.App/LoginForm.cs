using System.Drawing.Drawing2D;

namespace Omnia.Metadata.App;

public sealed class LoginForm : Form
{
    public string AuthenticatedEmail { get; private set; } = string.Empty;
    public string SessionToken { get; private set; } = string.Empty;
    public string Role { get; private set; } = "user";
    public string Plan { get; private set; } = "free";
    public int MonthlyFreeQueriesUsed { get; private set; }
    public int OneOffQueriesAvailable { get; private set; }

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
            RowCount = 8,
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
        google.Image = CreateGoogleIcon();
        google.ImageAlign = ContentAlignment.MiddleLeft;
        google.TextImageRelation = TextImageRelation.ImageBeforeText;
        google.Click += async (_, _) => await ProviderLogin(google, "google", "Google");

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

    static Bitmap CreateGoogleIcon()
    {
        var icon = new Bitmap(18, 18);
        using var graphics = Graphics.FromImage(icon);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var red = new Pen(Color.FromArgb(234, 67, 53), 3);
        using var yellow = new Pen(Color.FromArgb(251, 188, 5), 3);
        using var green = new Pen(Color.FromArgb(52, 168, 83), 3);
        using var blue = new Pen(Color.FromArgb(66, 133, 244), 3);
        var bounds = new Rectangle(2, 2, 14, 14);
        graphics.DrawArc(red, bounds, 205, 75);
        graphics.DrawArc(yellow, bounds, 125, 75);
        graphics.DrawArc(green, bounds, 35, 75);
        graphics.DrawArc(blue, bounds, 300, 65);
        graphics.DrawLine(blue, 9, 9, 16, 9);
        return icon;
    }

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
            Role = result.Role;
            Plan = result.Plan;
            MonthlyFreeQueriesUsed = result.MonthlyFreeQueriesUsed;
            OneOffQueriesAvailable = result.OneOffQueriesAvailable;
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
            Role = result.Role;
            Plan = result.Plan;
            MonthlyFreeQueriesUsed = result.MonthlyFreeQueriesUsed;
            OneOffQueriesAvailable = result.OneOffQueriesAvailable;
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
