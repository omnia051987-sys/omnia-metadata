# Omnia Metadata

Primeiro incremento: versão Proprietário local para validação técnica. O núcleo calcula SHA-256, identifica tipos básicos e extrai sinais iniciais de JPEG/PDF. Não há autenticação, cobrança, nuvem ou publicação nesta etapa.

## Executar

```powershell
dotnet run --project .\src\Omnia.Metadata.App\Omnia.Metadata.App.csproj
```

## Decisões iniciais

- Processamento local por padrão; arquivos não são copiados pelo aplicativo.
- `Omnia.Metadata.Core` é independente da interface para permitir testes e futuras UIs.
- Ausência de dado é explicitamente marcada como “Não encontrado nos dados disponíveis.”
- Indícios não são apresentados como fatos.
- A versão comercial exigirá backend seguro para identidade, planos e Store; nenhuma chave será embutida no cliente.

## Limitações atuais

Esta é uma fundação executável, não a versão final de produção. Ainda faltam leitores completos EXIF/IPTC/XMP, vídeo, documentos Office, C2PA, autenticação, assinatura, instalador MSIX, testes automatizados e materiais de Store.
