using Omnia.Metadata.Core;
namespace Omnia.Metadata.App;
public sealed class LoginForm : Form
{
    public string AuthenticatedEmail { get; private set; } = "";
    readonly TextBox email = new() { Width = 320, PlaceholderText = "E-mail" };
    readonly TextBox password = new() { Width = 320, PlaceholderText = "Senha", UseSystemPasswordChar = true };
    public LoginForm()
    {
        Text = "Omnia Metadata — Login"; Width = 520; Height = 560; MinimumSize = new(460, 500); StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new(28), BackColor = Color.White };
        layout.RowStyles.Add(new(SizeType.Absolute, 54)); layout.RowStyles.Add(new(SizeType.Absolute, 54)); layout.RowStyles.Add(new(SizeType.Absolute, 42)); layout.RowStyles.Add(new(SizeType.Absolute, 42)); layout.RowStyles.Add(new(SizeType.Absolute, 44)); layout.RowStyles.Add(new(SizeType.Absolute, 44)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        var title = new Label { Text = "OMNIA METADATA", Font = new Font("Segoe UI", 20, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(21, 45, 90) };
        var note = new Label { Text = "Acesse a análise técnica com uma conta verificada.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(70, 70, 70) };
        email.Dock = DockStyle.Fill; password.Dock = DockStyle.Fill; email.Margin = new(0, 6, 0, 6); password.Margin = new(0, 6, 0, 6);
        var login = ActionButton("Entrar com e-mail"); login.Click += (_, _) => EmailLogin();
        var create = ActionButton("Criar conta"); create.Click += (_, _) => { using var form = new CreateAccountForm(); form.ShowDialog(this); };
        var providers = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Controls = { Provider("Google"), Provider("GitHub"), Provider("Microsoft") } };
        layout.Controls.Add(title, 0, 0); layout.Controls.Add(note, 0, 1); layout.Controls.Add(email, 0, 2); layout.Controls.Add(password, 0, 3); layout.Controls.Add(login, 0, 4); layout.Controls.Add(create, 0, 5); layout.Controls.Add(providers, 0, 6); Controls.Add(layout); AcceptButton = login;
    }
    static Button ActionButton(string text) => new() { Text = text, Dock = DockStyle.Fill, Height = 34, FlatStyle = FlatStyle.System };
    Button Provider(string name) { var b = new Button { Text = name, Width = 132, Height = 32, Margin = new(0, 4, 6, 0) }; b.Click += (_, _) => MessageBox.Show($"Login {name} ainda depende da configuração OAuth do servidor. Nenhuma identidade foi autenticada.", "Integração de login", MessageBoxButtons.OK, MessageBoxIcon.Information); return b; }
    void EmailLogin() { if (!OwnerPolicy.IsOwnerEmail(email.Text)) { MessageBox.Show("E-mail não autorizado para a edição Proprietário. Apenas as contas proprietárias configuradas podem entrar nesta versão.", "Acesso negado", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; } if (string.IsNullOrWhiteSpace(password.Text)) { MessageBox.Show("Informe a senha.", "Login", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; } AuthenticatedEmail = email.Text.Trim(); DialogResult = DialogResult.OK; Close(); }
}
