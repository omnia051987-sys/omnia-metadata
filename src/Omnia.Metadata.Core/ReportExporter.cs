using System.Text;
namespace Omnia.Metadata.Core;
public static class ReportExporter
{
    public static string ToText(AnalysisReport r) { var b = new StringBuilder(); b.AppendLine("OMNIA METADATA — RELATÓRIO TÉCNICO"); b.AppendLine($"Arquivo: {r.FileName}"); b.AppendLine($"Hash SHA-256: {r.Sha256}"); b.AppendLine($"Análise (UTC): {r.AnalyzedAtUtc:O}"); b.AppendLine($"Status: {r.Status}"); b.AppendLine(); foreach (var f in r.Fields) b.AppendLine($"[{f.Kind}] {f.Name}: {f.Value}"); b.AppendLine(); b.AppendLine("Limitações:"); foreach (var l in r.Limitations) b.AppendLine("- " + l); b.AppendLine("Este relatório não substitui perícia oficial, exame laboratorial ou validação judicial."); return b.ToString(); }
}
