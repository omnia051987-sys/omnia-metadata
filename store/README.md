# Pacote para Microsoft Store

Esta pasta contém a preparação do aplicativo para empacotamento MSIX. O fluxo não publica nada na Microsoft Store.

Antes da submissão, o Partner Center precisa fornecer a identidade reservada do aplicativo e o publisher. Substitua no `Package.appxmanifest`:

- `Identity Name`
- `Identity Publisher`
- versão do pacote, mantendo o incremento obrigatório entre submissões

O script `build-msix.ps1` publica o executável self-contained, monta a estrutura MSIX e chama `makeappx.exe`. Ele recusa a identidade provisória por padrão. Para uma validação local do formato, sem intenção de instalar ou publicar, é possível usar `-AllowPlaceholderIdentity`.

O pacote final ainda deve passar por assinatura e validações do Partner Center. Nenhuma compra, serviço pago ou publicação é acionada por estes arquivos.
