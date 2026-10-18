namespace Omnia.Metadata.Core;

public sealed record MetadataField(string Name, string Value, FieldKind Kind = FieldKind.Confirmed);
public enum FieldKind { Confirmed, Indication, Alert, Missing, Inconclusive, Unverifiable }

public sealed record class AnalysisReport
{
    public required string FileName { get; init; }
    public required string DetectedType { get; init; }
    public required string Extension { get; init; }
    public required long SizeBytes { get; init; }
    public required string Sha256 { get; init; }
    public required DateTime AnalyzedAtUtc { get; init; }
    public required string Status { get; init; }
    public required IReadOnlyList<MetadataField> Fields { get; init; }
    public required IReadOnlyList<string> Limitations { get; init; }
    public string ExecutiveSummary { get; init; } = "Análise técnica baseada exclusivamente nos dados disponíveis.";
}
