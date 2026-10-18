# Omnia Auth Server

Base local do backend de autenticação. Executar com `dotnet run --project server/Omnia.AuthServer.csproj`.

Endpoints implementados: `/health`, solicitação de confirmação, confirmação de token e revogação. Tokens expiram em 24 horas e são consumidos uma única vez.

OAuth, SMTP real, banco persistente, MFA e sessões distribuídas ainda exigem configuração de produção. O armazenamento atual é deliberadamente temporário para desenvolvimento e não deve ser usado em publicação.
