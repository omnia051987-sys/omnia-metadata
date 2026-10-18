using System.Security.Cryptography;
namespace Omnia.Metadata.Core;

public sealed record VerificationRequest(string Email, string Token, DateTime ExpiresUtc, string ConfirmationUrl);

public static class EmailVerification
{
    public const string Sender = "omnia051987@gmail.com";
    public static VerificationRequest Create(string email, string baseUrl = "https://auth.omniametadata.example/confirm")
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+','-').Replace('/','_').TrimEnd('=');
        var expires = DateTime.UtcNow.AddHours(24);
        return new(email.Trim(), token, expires, $"{baseUrl}?email={Uri.EscapeDataString(email.Trim())}&token={token}");
    }
    public static string Subject => "Confirme seu e-mail — Omnia Metadata";
    public static string Body(VerificationRequest request) => $"Olá,\n\nRecebemos um pedido de cadastro no Omnia Metadata para {request.Email}.\n\nConfirme seu e-mail clicando no link abaixo:\n{request.ConfirmationUrl}\n\nEste link expira em 24 horas e pode ser usado uma única vez. Se você não solicitou o cadastro, ignore esta mensagem.\n\nOmnia Metadata\nRemetente: {Sender}";
}
