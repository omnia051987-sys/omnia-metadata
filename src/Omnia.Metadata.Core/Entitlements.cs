using System.Text.RegularExpressions;

namespace Omnia.Metadata.Core;

public enum AppEdition { Proprietario, Free, Assinante }

public sealed record EntitlementState(AppEdition Edition, int MonthlyFreeQueriesUsed, int OneOffQueriesAvailable)
{
    public bool CanViewGps => Edition is AppEdition.Proprietario or AppEdition.Assinante;
    public bool CanAnalyze => Edition is AppEdition.Proprietario or AppEdition.Assinante || MonthlyFreeQueriesUsed < 1 || OneOffQueriesAvailable > 0;
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
    static readonly string[] GenericTransitions =
    [
        "além disso", "por outro lado", "nesse sentido", "dessa forma", "em outras palavras",
        "em resumo", "em conclusão", "por fim", "de maneira geral", "vale destacar",
        "é importante", "um ponto importante", "a seguir", "em suma", "consequentemente",
        "however", "moreover", "in conclusion", "it is important", "in summary"
    ];

    static readonly string[] AssistantPhrases =
    [
        "como modelo de linguagem", "como ia", "não posso ajudar", "não tenho acesso",
        "posso ajudar", "espero que isso ajude", "se precisar, posso", "com base nas informações",
        "aqui está uma", "vamos explorar", "é importante observar", "vale ressaltar"
    ];

    public static TextAiAssessment Assess(string text)
    {
        var source = text ?? string.Empty;
        var normalized = Regex.Replace(source, @"\s+", " ").Trim();
        if (normalized.Length < 120)
            return new("Análise inconclusiva", 0,
                ["Texto curto demais para uma triagem responsável."],
                "Detectores de texto não comprovam autoria ou uso de IA.");

        var signals = new List<string>();
        var groups = 0;
        var score = 0;
        var words = Regex.Matches(normalized, @"[\p{L}\p{M}\p{N}]+(?:['’\-][\p{L}\p{M}\p{N}]+)?")
            .Select(m => m.Value.ToLowerInvariant()).ToArray();
        var sentences = Regex.Split(normalized, @"(?<=[.!?])\s+")
            .Where(s => s.Trim().Length > 0).ToArray();
        var sentenceLengths = sentences.Select(s => Regex.Matches(s, @"[\p{L}\p{M}\p{N}]+\b").Count).Where(n => n > 0).ToArray();
        var paragraphs = source.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var assistantHits = CountPhrases(normalized, AssistantPhrases);
        if (assistantHits > 0)
        {
            score += Math.Min(5, assistantHits * 2);
            groups++;
            signals.Add($"Foram encontrados {assistantHits} marcador(es) de linguagem típica de assistentes ou respostas padronizadas.");
        }

        var transitionHits = CountPhrases(normalized, GenericTransitions);
        var transitionRate = sentences.Length == 0 ? 0 : (double)transitionHits / sentences.Length;
        if (transitionHits >= 3 || transitionRate >= 0.18)
        {
            score += transitionHits >= 6 ? 3 : 2;
            groups++;
            signals.Add($"Alta concentração de conectores discursivos previsíveis ({transitionHits} ocorrência(s) em {sentences.Length} frase(s)).");
        }

        var headings = Regex.Matches(source, @"(?m)^\s{0,3}(?:#{1,6}\s+|[A-ZÁÉÍÓÚÃÕÇ][^.!?]{2,60}:\s*$)").Count;
        var bullets = Regex.Matches(source, @"(?m)^\s*(?:[-*•]|\d+[.)])\s+").Count;
        var markdown = Regex.Matches(source, @"(?:\*\*[^*]+\*\*|`[^`]+`|^\s*#{1,6}\s+)", RegexOptions.Multiline).Count;
        if (headings >= 2 || bullets >= 3 || markdown >= 3)
        {
            score += 2;
            groups++;
            signals.Add($"Estrutura editorial muito regular ({headings} título(s), {bullets} item(ns) e {markdown} marcação(ões)); isso é compatível com texto assistido, mas também pode ser humano.");
        }

        if (sentenceLengths.Length >= 5)
        {
            var mean = sentenceLengths.Average();
            var variance = sentenceLengths.Select(n => Math.Pow(n - mean, 2)).Average();
            var coefficient = mean == 0 ? 0 : Math.Sqrt(variance) / mean;
            if (coefficient < 0.28)
            {
                score += 2;
                groups++;
                signals.Add("Comprimento das frases excessivamente uniforme, um padrão estatístico compatível com geração automática.");
            }
            else if (coefficient > 0.85)
            {
                signals.Add("Variação alta no comprimento das frases; esse sinal reduz a suspeita, mas não exclui IA.");
                score = Math.Max(0, score - 1);
            }
        }

        if (words.Length >= 150)
        {
            var distinctRate = words.Distinct().Count() / (double)words.Length;
            var repeatedBigrams = CountRepeatedBigrams(words);
            if (distinctRate < 0.42 || repeatedBigrams >= 3)
            {
                score += 1;
                groups++;
                signals.Add($"Há baixa diversidade lexical ou repetição de combinações ({distinctRate:P0} de vocabulário distinto; {repeatedBigrams} bigrama(s) repetido(s)).");
            }
        }

        if (paragraphs.Length >= 3)
        {
            var sizes = paragraphs.Select(p => p.Length).ToArray();
            var balanced = sizes.Average() > 0 && sizes.Max() / sizes.Average() < 2.4;
            if (balanced)
            {
                score += 1;
                groups++;
                signals.Add("Parágrafos com extensão e cadência muito equilibradas; sinal complementar, não conclusivo.");
            }
        }

        score = Math.Clamp(score, 0, 12);
        var classification = score >= 8 && groups >= 3
            ? "Suspeita elevada de texto compatível com IA"
            : score >= 5 && groups >= 2
                ? "Suspeita moderada de texto compatível com IA"
                : score >= 2
                    ? "Indícios fracos compatíveis com IA"
                    : "Nenhum indício textual suficiente identificado";
        if (signals.Count == 0)
            signals.Add("Nenhum marcador estatístico ou linguístico relevante foi localizado.");
        signals.Add($"Triagem composta: {groups} grupo(s) de sinais independentes contribuíram para a pontuação.");
        return new(classification, score, signals,
            "Resultado probabilístico: texto humano revisado, texto traduzido e texto gerado por IA podem compartilhar esses padrões. A análise não prova autoria e não deve ser usada isoladamente para decisão disciplinar, judicial ou administrativa.");
    }

    static int CountPhrases(string text, IEnumerable<string> phrases) =>
        phrases.Sum(phrase => Regex.Matches(text, Regex.Escape(phrase), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count);

    static int CountRepeatedBigrams(string[] words)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < words.Length - 1; i++)
        {
            var bigram = $"{words[i]} {words[i + 1]}";
            counts[bigram] = counts.GetValueOrDefault(bigram) + 1;
        }
        return counts.Count(pair => pair.Value >= 3);
    }
}
