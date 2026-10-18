namespace Omnia.Metadata.Core;

public enum AppEdition { Proprietario, Free, Assinante }

public sealed record EntitlementState(AppEdition Edition, int MonthlyFreeQueriesUsed, int OneOffQueriesAvailable)
{
    public bool CanViewGps => Edition is AppEdition.Proprietario or AppEdition.Assinante;
    public bool CanAnalyze => Edition == AppEdition.Proprietario || MonthlyFreeQueriesUsed < 1 || OneOffQueriesAvailable > 0;
    public string QueryMessage => Edition == AppEdition.Proprietario ? "Consultas ilimitadas (Proprietário)." : MonthlyFreeQueriesUsed < 1 ? "Consulta gratuita mensal disponível." : "Consulta gratuita utilizada. Consulta avulsa: US$ 1,00.";
}

public static class ReportAccessFilter
{
    public static IReadOnlyList<MetadataField> Apply(IReadOnlyList<MetadataField> fields, EntitlementState state)
    {
        if (state.CanViewGps) return fields;
        return fields.Select(f => f.Name.Contains("GPS", StringComparison.OrdinalIgnoreCase) || f.Name.Contains("Latitude", StringComparison.OrdinalIgnoreCase) || f.Name.Contains("Longitude", StringComparison.OrdinalIgnoreCase)
            ? new MetadataField(f.Name, "Oculto no plano FREE. Disponível mediante assinatura ou consulta avulsa de US$ 1,00.", FieldKind.Unverifiable) : f).ToList();
    }
}

public sealed record TextAiAssessment(string Classification, int Score, IReadOnlyList<string> Signals, string Limitation);

public static class TextAiDetector
{
    public static TextAiAssessment Assess(string text)
    {
        var signals = new List<string>(); var score = 0;
        var normalized = text.Trim();
        if (normalized.Length < 80) return new("Análise inconclusiva", 0, ["Texto curto demais para uma triagem responsável."], "Detectores de texto não comprovam autoria ou uso de IA.");
        if (normalized.Contains("como modelo de linguagem", StringComparison.OrdinalIgnoreCase) || normalized.Contains("não posso ajudar", StringComparison.OrdinalIgnoreCase)) { score += 3; signals.Add("Expressão frequentemente associada a respostas de assistentes."); }
        if (normalized.Split('\n').Count(x => x.Trim().Length == 0) >= 2) { score++; signals.Add("Estrutura altamente organizada; sinal fraco e não exclusivo de IA."); }
        if (normalized.Contains("em conclusão", StringComparison.OrdinalIgnoreCase) && normalized.Contains("é importante", StringComparison.OrdinalIgnoreCase)) { score++; signals.Add("Padrões linguísticos genéricos; sinal fraco e não exclusivo de IA."); }
        var classification = score >= 3 ? "Suspeita técnica de texto compatível com IA" : score > 0 ? "Indícios fracos compatíveis com IA" : "Nenhum indício textual suficiente identificado";
        return new(classification, score, signals.Count == 0 ? ["Nenhum marcador textual específico foi localizado."] : signals, "Resultado heurístico: não prova nem exclui autoria humana ou uso de IA.");
    }
}
