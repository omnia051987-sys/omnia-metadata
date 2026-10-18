namespace Omnia.Metadata.App;

public sealed class CreateAccountForm : Form
{
    readonly TextBox email = new() { PlaceholderText = "E-mail", Dock = DockStyle.Fill };
    readonly TextBox password = new() { PlaceholderText = "Senha (mínimo de 12 caracteres)", UseSystemPasswordChar = true, Dock = DockStyle.Fill };
    readonly TextBox confirmPassword = new() { PlaceholderText = "Confirmar senha", UseSystemPasswordChar = true, Dock = DockStyle.Fill };
    readonly Button create = new() { Text = "Criar conta", Dock = DockStyle.Fill };
    readonly AuthClient auth = new();

    public CreateAccountForm()
    {
        Text = "Omnia Metadata — Criar conta";
        Width = 520;
        Height = 390;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28),
            ColumnCount = 1,
            RowCount = 7,
            BackColor = Color.White
        };
        grid.RowStyles.Add(new(SizeType.Absolute, 44));
        grid.RowStyles.Add(new(SizeType.Absolute, 48));
        grid.RowStyles.Add(new(SizeType.Absolute, 42));
        grid.RowStyles.Add(new(SizeType.Absolute, 42));
        grid.RowStyles.Add(new(SizeType.Absolute, 42));
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
            Text = "Crie sua conta diretamente com e-mail e senha. A confirmação por link foi removida.",
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            ForeColor = Color.FromArgb(70, 70, 70)
        };
        email.Margin = new Padding(0, 4, 0, 4);
        password.Margin = new Padding(0, 4, 0, 4);
        confirmPassword.Margin = new Padding(0, 4, 0, 4);
        create.Click += async (_, _) => await CreateAsync();

        grid.Controls.Add(title, 0, 0);
        grid.Controls.Add(info, 0, 1);
        grid.Controls.Add(email, 0, 2);
        grid.Controls.Add(password, 0, 3);
        grid.Controls.Add(confirmPassword, 0, 4);
        grid.Controls.Add(create, 0, 5);
        Controls.Add(grid);
        AcceptButton = create;
    }

    async Task CreateAsync()
    {
        var address = email.Text.Trim();
        if (string.IsNullOrWhiteSpace(address) || !address.Contains('@'))
        {
            MessageBox.Show("Informe um e-mail válido.", "Criar conta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (password.Text.Length < 12)
        {
            MessageBox.Show("A senha deve ter pelo menos 12 caracteres.", "Criar conta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!string.Equals(password.Text, confirmPassword.Text, StringComparison.Ordinal))
        {
            MessageBox.Show("A confirmação de senha não coincide.", "Criar conta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        create.Enabled = false;
        try
        {
            await auth.RegisterAsync(address, password.Text);
            MessageBox.Show(
                "Conta criada. Agora entre com seu e-mail e senha.",
                "Cadastro concluído",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Não foi possível criar a conta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed) create.Enabled = true;
        }
    }
}
