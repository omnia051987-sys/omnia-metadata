namespace Omnia.Metadata.App;
public sealed class CreateAccountForm : Form
{
    public CreateAccountForm()
    {
        Text = "Omnia Metadata — Criar conta"; Width = 480; Height = 330; StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(24), ColumnCount = 1, RowCount = 6, BackColor = Color.White };
        grid.RowStyles.Add(new(SizeType.Absolute, 42)); grid.RowStyles.Add(new(SizeType.Absolute, 42)); grid.RowStyles.Add(new(SizeType.Absolute, 42)); grid.RowStyles.Add(new(SizeType.Absolute, 42)); grid.RowStyles.Add(new(SizeType.Absolute, 42)); grid.RowStyles.Add(new(SizeType.Percent, 100));
        var title = new Label { Text = "Criar conta", Font = new Font("Segoe UI", 16, FontStyle.Bold), Dock = DockStyle.Fill, ForeColor = Color.FromArgb(21,45,90) };
        var email = new TextBox { PlaceholderText = "E-mail", Dock = DockStyle.Fill }; var password = new TextBox { PlaceholderText = "Senha", UseSystemPasswordChar = true, Dock = DockStyle.Fill }; var confirm = new TextBox { PlaceholderText = "Confirmar senha", UseSystemPasswordChar = true, Dock = DockStyle.Fill }; var create = new Button { Text = "Enviar confirmação por e-mail", Dock = DockStyle.Fill };
        create.Click += (_, _) => MessageBox.Show("A conta será criada após confirmação do link enviado por e-mail. O envio real depende do servidor SMTP configurado.", "Confirmação de e-mail", MessageBoxButtons.OK, MessageBoxIcon.Information);
        grid.Controls.Add(title,0,0); grid.Controls.Add(email,0,1); grid.Controls.Add(password,0,2); grid.Controls.Add(confirm,0,3); grid.Controls.Add(create,0,4); Controls.Add(grid);
    }
}
