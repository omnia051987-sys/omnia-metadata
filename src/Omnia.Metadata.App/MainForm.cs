using Omnia.Metadata.Core;
using Windows.Services.Store;

namespace Omnia.Metadata.App;

public sealed class MainForm : Form
{
    readonly ListBox files = new() { Dock = DockStyle.Fill };
    readonly RichTextBox report = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White };
    readonly FileAnalyzer analyzer = new();
    readonly string authenticatedEmail;
    readonly string role;
    readonly string plan;
    readonly int monthlyFreeQueriesUsed;
    readonly int oneOffQueriesAvailable;
    readonly StoreBillingClient billing = new();
    AnalysisReport? current;
    EntitlementState access;

    public MainForm(string authenticatedEmail, string role, string plan, int monthlyFreeQueriesUsed, int oneOffQueriesAvailable)
    {
        this.authenticatedEmail = authenticatedEmail;
        this.role = role;
        this.plan = plan;
        this.monthlyFreeQueriesUsed = monthlyFreeQueriesUsed;
        this.oneOffQueriesAvailable = oneOffQueriesAvailable;

        var edition = role.Equals("owner", StringComparison.OrdinalIgnoreCase) || plan.Equals("owner", StringComparison.OrdinalIgnoreCase)
            ? AppEdition.Proprietario
            : plan.Equals("subscriber", StringComparison.OrdinalIgnoreCase) ? AppEdition.Assinante : AppEdition.Free;
        access = new(edition, monthlyFreeQueriesUsed, oneOffQueriesAvailable);

        Text = $"Omnia Metadata — {edition} — {authenticatedEmail}";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Width = 1160;
        Height = 720;
        MinimumSize = new(900, 560);
        StartPosition = FormStartPosition.CenterScreen;

        var open = ActionButton("Selecionar arquivo");
        var export = ActionButton("Exportar .txt");
        export.Enabled = false;
        var ai = ActionButton("Detecção de IA");
        var textAi = ActionButton("Detecção de IA em texto");
        var edit = ActionButton("Detecção de edição de mídia");
        var about = ActionButton("Sobre");
        var subscribe = ActionButton("Assinar Pro — US$ 4,99/mês");
        var oneOff = ActionButton("Comprar consulta — US$ 0,99");

        open.Click += async (_, _) => await OpenFile();
        export.Click += (_, _) => Export();
        ai.Click += (_, _) => ShowCategory("IA e proveniência", "Detecção de IA");
        textAi.Click += (_, _) => ShowTextAi();
        edit.Click += (_, _) => ShowCategory("Edição e processamento", "Detecção de edição de mídia");
        about.Click += (_, _) => ShowAbout();
        subscribe.Click += async (_, _) => await PurchaseSubscription(subscribe);
        oneOff.Click += async (_, _) => await PurchaseOneOff(oneOff);

        var accessLabel = new Label { Text = access.QueryMessage, AutoSize = true, Padding = new(8, 7, 0, 0), ForeColor = Color.FromArgb(21, 45, 90) };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 112, Padding = new(8), WrapContents = true, AutoScroll = true };
        top.Controls.AddRange([open, export, ai, textAi, edit, about, subscribe, oneOff, accessLabel]);

        files.SelectedIndexChanged += (_, _) => { if (files.SelectedItem is string path) _ = Analyze(path, export); };
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 320 };
        split.Panel1.Controls.Add(files);
        split.Panel2.Controls.Add(report);
        Controls.Add(split);
        Controls.Add(top);

        report.Text = $"Usuário autenticado: {authenticatedEmail}\n\nPlano: {edition}\n\nSelecione um arquivo para iniciar a análise local.\n\nNenhum arquivo é enviado para a nuvem nesta versão.";
        Load += async (_, _) => await RefreshStoreEntitlement(accessLabel);
    }

    static Button ActionButton(string text) => new() { Text = text, AutoSize = true, Height = 34, FlatStyle = FlatStyle.System, Margin = new(3) };

    internal void LoadFilesForScreenshot(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(File.Exists)) files.Items.Add(path);
        if (files.Items.Count > 0) files.SelectedIndex = 0;
    }

    internal void ShowTextAiForScreenshot(string text) => ShowTextAi(text);

    async Task RefreshStoreEntitlement(Label accessLabel)
    {
        if (access.Edition == AppEdition.Proprietario) return;
        var subscription = await billing.HasActiveSubscriptionAsync();
        if (subscription)
        {
            access = new EntitlementState(AppEdition.Assinante, monthlyFreeQueriesUsed, oneOffQueriesAvailable);
            accessLabel.Text = "Assinatura Microsoft Store ativa — GPS liberado.";
            Text = $"Omnia Metadata — Assinante — {authenticatedEmail}";
        }
    }

    async Task PurchaseSubscription(Button button)
    {
        await RunPurchase(button, billing.PurchaseSubscriptionAsync);
    }

    async Task PurchaseOneOff(Button button)
    {
        await RunPurchase(button, billing.PurchaseOneOffAsync);
    }

    async Task RunPurchase(Button button, Func<Task<StorePurchaseResult>> purchase)
    {
        button.Enabled = false;
        try
        {
            var result = await purchase();
            MessageBox.Show(result.Message + "\n\nA cobrança é processada exclusivamente pela Microsoft Store; o Omnia Metadata não armazena dados de cartão.", "Microsoft Store", MessageBoxButtons.OK,
                result.Status is StorePurchaseStatus.Succeeded or StorePurchaseStatus.AlreadyPurchased ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            if (result.Status is StorePurchaseStatus.Succeeded or StorePurchaseStatus.AlreadyPurchased)
                await RefreshStoreEntitlement(new Label());
        }
        finally { button.Enabled = true; }
    }

    async Task OpenFile()
    {
        using var dialog = new OpenFileDialog { Filter = "Todos os arquivos|*.*", Multiselect = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            foreach (var path in dialog.FileNames) files.Items.Add(path);
        await Task.CompletedTask;
    }

    async Task Analyze(string path, Button export)
    {
        try
        {
            report.Text = "Analisando localmente…";
            current = await analyzer.AnalyzeAsync(path);
            current = current with { Fields = ReportAccessFilter.Apply(current.Fields, access) };
            report.Text = ReportExporter.ToText(current);
            export.Enabled = true;
        }
        catch (Exception ex)
        {
            report.Text = "Análise inconclusiva.\n\nErro técnico: " + ex.Message;
            export.Enabled = false;
        }
    }

    void Export()
    {
        if (current is null) return;
        using var dialog = new SaveFileDialog { Filter = "Relatório TXT|*.txt", FileName = current.FileName + ".txt" };
        if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllText(dialog.FileName, ReportExporter.ToText(current));
    }

    void ShowCategory(string category, string title)
    {
        if (current is null)
        {
            MessageBox.Show("Selecione e analise um arquivo primeiro.", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var items = current.Fields.Where(field => field.Name.Equals(category, StringComparison.OrdinalIgnoreCase)
            || (category.StartsWith("IA", StringComparison.Ordinal) && (field.Name.Contains("IA") || field.Name.Contains("proveniência", StringComparison.OrdinalIgnoreCase)))
            || (category.StartsWith("Edição", StringComparison.Ordinal) && field.Name.Contains("Edição", StringComparison.OrdinalIgnoreCase))).ToList();
        report.Text = title.ToUpperInvariant() + Environment.NewLine + Environment.NewLine
            + (items.Count == 0 ? "Análise inconclusiva. Não há dados específicos suficientes." : string.Join(Environment.NewLine, items.Select(field => $"[{field.Kind}] {field.Name}: {field.Value}")))
            + Environment.NewLine + Environment.NewLine + "A ausência de indícios não prova autenticidade.";
    }

    void ShowAbout() => MessageBox.Show("Omnia Metadata — Metadados de Arquivos\nVersão: 0.7.2\nEdição: " + access.Edition + "\n\nAnálise local técnica de metadados e integridade.\nA detecção de IA e edição apresenta indícios probabilísticos, não conclusões absolutas.", "Sobre", MessageBoxButtons.OK, MessageBoxIcon.Information);

    void ShowTextAi(string? initialText = null)
    {
        var limit = access.Edition == AppEdition.Free ? 1000 : 20000;
        using var dialog = new Form { Text = "Detecção de IA em texto", Width = 700, Height = 500, StartPosition = FormStartPosition.CenterParent };
        var input = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, MaxLength = limit };
        input.Text = initialText ?? string.Empty;
        var go = new Button { Text = "Analisar texto", Dock = DockStyle.Bottom, Height = 38 };
        var info = new Label { Text = $"Cole até {limit:N0} caracteres. Triagem composta; não é prova de autoria.", Dock = DockStyle.Top, Height = 34 };
        dialog.Controls.Add(input); dialog.Controls.Add(go); dialog.Controls.Add(info);
        go.Click += (_, _) =>
        {
            var assessment = TextAiDetector.Assess(input.Text);
            MessageBox.Show($"{assessment.Classification}\nPontuação composta: {assessment.Score}/12\n\n{string.Join("\n", assessment.Signals)}\n\n{assessment.Limitation}", "Detecção de IA em texto", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        dialog.ShowDialog(this);
    }
}
