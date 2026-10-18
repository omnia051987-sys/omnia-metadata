using System.Security.Cryptography;
using System.Text;

namespace Omnia.Metadata.Core;

public sealed class FileAnalyzer
{
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png",
        [".gif"] = "image/gif", [".mp4"] = "video/mp4", [".mov"] = "video/quicktime",
        [".pdf"] = "application/pdf", [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    };

    public async Task<AnalysisReport> AnalyzeAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Arquivo não encontrado.", path);
        var info = new FileInfo(path);
        using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        var ext = info.Extension.ToLowerInvariant();
        var fields = new List<MetadataField>
        {
            new("MIME type", MimeTypes.GetValueOrDefault(ext, "application/octet-stream")),
            new("Data de modificação", info.LastWriteTimeUtc.ToString("O")),
            new("Tipo detectado", DetectType(stream, ext))
        };
        if (ext is ".jpg" or ".jpeg") fields.AddRange(ReadJpegHints(path));
        else if (ext == ".pdf") fields.AddRange(ReadPdfHints(path));
        else fields.Add(new("Metadados específicos", "Formato não suportado para análise detalhada.", FieldKind.Missing));
        fields.AddRange(BuildTransferAssessment(fields, ext));
        fields.AddRange(BuildIntegrityAssessment(fields, ext));
        return new AnalysisReport { FileName = info.Name, DetectedType = fields[2].Value, Extension = ext,
            SizeBytes = info.Length, Sha256 = Convert.ToHexString(hash), AnalyzedAtUtc = DateTime.UtcNow,
            Status = "Concluída", Fields = fields, Limitations = ["A análise não prova autenticidade absoluta.", "Ausência de indícios não prova autenticidade."] };
    }

    private static IEnumerable<MetadataField> BuildIntegrityAssessment(IReadOnlyCollection<MetadataField> fields, string ext)
    {
        var software = string.Join(" ", fields.Where(f => f.Name is "Software" or "Creator" or "Producer").Select(f => f.Value));
        var editorWords = new[] { "photoshop", "lightroom", "gimp", "illustrator", "canva", "premiere", "davinci", "ffmpeg", "adobe", "microsoft word", "libreoffice" };
        var aiWords = new[] { "midjourney", "stable diffusion", "dall-e", "dalle", "firefly", "comfyui", "automatic1111", "generative" };
        var hasEditor = editorWords.Any(x => software.Contains(x, StringComparison.OrdinalIgnoreCase));
        var hasAi = aiWords.Any(x => software.Contains(x, StringComparison.OrdinalIgnoreCase));
        var weakAiImageSignals = ext is ".png" or ".webp" && !fields.Any(f => f.Name == "EXIF" && f.Kind == FieldKind.Confirmed);
        yield return new("Edição e processamento", hasEditor ? "Assinatura de software compatível com edição ou processamento encontrada; isso é um indício técnico." : "Nenhum indício técnico conclusivo de edição foi identificado.", hasEditor ? FieldKind.Indication : FieldKind.Inconclusive);
        yield return new("IA e proveniência", hasAi ? "Indício compatível com processamento por IA encontrado nos metadados; não constitui prova de autoria ou geração por IA." : weakAiImageSignals ? "Suspeita técnica de processamento por IA: formato sem EXIF e ausência de proveniência verificável. Sinal probabilístico, não confirmação." : "Nenhum indício suficiente relacionado a IA foi identificado nos dados disponíveis.", hasAi || weakAiImageSignals ? FieldKind.Indication : FieldKind.Inconclusive);
        yield return new("Conclusão de autenticidade", "Análise inconclusiva. Ausência de indícios não prova que o arquivo seja original ou não tenha sido processado.", FieldKind.Inconclusive);
        if (ext is ".txt" or ".csv" or ".json" or ".xml" or ".docx" or ".pdf")
            yield return new("Texto e estrutura", "A presença de texto, estilo ou estrutura compatível com edição não permite atribuir autoria humana ou uso de IA sem evidência adicional.", FieldKind.Unverifiable);
    }

    private static IEnumerable<MetadataField> BuildTransferAssessment(IReadOnlyCollection<MetadataField> fields, string ext)
    {
        var hasExif = fields.Any(f => f.Name == "EXIF" && f.Kind == FieldKind.Confirmed);
        var hasGps = fields.Any(f => f.Name == "GPS" && f.Kind == FieldKind.Confirmed);
        var software = fields.FirstOrDefault(f => f.Name == "Software")?.Value ?? "";
        yield return new("Metadados preservados", hasExif || fields.Any(f => f.Name is "Author" or "Creator" or "Producer" && f.Kind == FieldKind.Confirmed)
            ? "Há metadados internos preservados no arquivo recebido." : "Nenhum metadado interno relevante foi localizado.", hasExif ? FieldKind.Confirmed : FieldKind.Missing);
        yield return new("GPS no arquivo recebido", hasGps ? "Dados de localização presentes no arquivo recebido." : "Não encontrado nos dados disponíveis.", hasGps ? FieldKind.Confirmed : FieldKind.Missing);
        yield return new("Recuperação de metadados removidos", "Não é possível recuperar com segurança metadados que não estejam presentes neste arquivo.", FieldKind.Unverifiable);
        if (ext is ".jpg" or ".jpeg")
        {
            var hint = software.Length > 0 ? "Assinatura de software encontrada; pode indicar edição, conversão ou processamento anterior." : "O arquivo pode ter sido recomprimido ou recebido por serviço de mensagens; não há dados suficientes para atribuir a origem.";
            yield return new("Transferência e recompressão", hint, FieldKind.Indication);
        }
        else
            yield return new("Transferência e conversão", "O arquivo recebido deve ser comparado ao original por hash e estrutura binária quando o original estiver disponível.", FieldKind.Indication);
    }

    private static string DetectType(Stream stream, string ext)
    {
        Span<byte> b = stackalloc byte[8]; stream.Position = 0; stream.ReadExactly(b);
        if (b[..4].SequenceEqual("%PDF"u8)) return "PDF";
        if (b[0] == 0xFF && b[1] == 0xD8) return "JPEG";
        if (b[..8].SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})) return "PNG";
        return MimeTypes.ContainsKey(ext) ? ext.TrimStart('.').ToUpperInvariant() : "Desconhecido";
    }
    private static IEnumerable<MetadataField> ReadJpegHints(string path)
    {
        var bytes = File.ReadAllBytes(path); var text = Encoding.Latin1.GetString(bytes);
        if (text.Contains("Exif", StringComparison.Ordinal)) yield return new("EXIF", "Bloco EXIF presente");
        else yield return new("EXIF", "Não encontrado nos dados disponíveis.", FieldKind.Missing);
        if (text.Contains("Photoshop", StringComparison.OrdinalIgnoreCase) || text.Contains("Lightroom", StringComparison.OrdinalIgnoreCase))
            yield return new("Software", "Assinatura de software encontrada; isso é um indício, não uma conclusão.", FieldKind.Indication);
        yield return new("GPS", "Não encontrado nos dados disponíveis.", FieldKind.Missing);
    }
    private static IEnumerable<MetadataField> ReadPdfHints(string path)
    {
        var text = Encoding.Latin1.GetString(File.ReadAllBytes(path));
        foreach (var key in new[] { "/Author", "/Creator", "/Producer", "/CreationDate", "/ModDate" })
        {
            var label = key.TrimStart('/');
            var value = ExtractPdfValue(text, key);
            yield return new(label, value ?? "Não encontrado nos dados disponíveis.", value is null ? FieldKind.Missing : FieldKind.Confirmed);
        }
    }

    private static string? ExtractPdfValue(string text, string key)
    {
        var start = text.IndexOf(key, StringComparison.Ordinal);
        if (start < 0) return null;
        var i = start + key.Length;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        if (i >= text.Length) return null;
        if (text[i] == '(')
        {
            var end = text.IndexOf(')', i + 1);
            return end > i ? text[(i + 1)..end].Trim() : null;
        }
        if (text[i] == '<' && i + 1 < text.Length && text[i + 1] == '<') return "Dicionário presente no documento";
        var finish = i;
        while (finish < text.Length && !char.IsWhiteSpace(text[finish]) && text[finish] != '/' && text[finish] != '>') finish++;
        return finish > i ? text[i..finish].Trim() : null;
    }
}
