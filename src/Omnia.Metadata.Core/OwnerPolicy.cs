namespace Omnia.Metadata.Core;

public static class OwnerPolicy
{
    private static readonly HashSet<string> Owners = new(StringComparer.OrdinalIgnoreCase)
    {
        "leohighway@gmail.com",
        "leonardolimabravo@hotmail.com",
        "omnia051987@gmail.com"
    };
    public static bool IsOwnerEmail(string email) => Owners.Contains(email.Trim());
}
