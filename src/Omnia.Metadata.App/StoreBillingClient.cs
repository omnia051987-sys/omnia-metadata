using Windows.Services.Store;

namespace Omnia.Metadata.App;

public sealed record StorePurchaseResult(StorePurchaseStatus Status, string ProductId, string Message);

public sealed class StoreBillingClient
{
    public const string SubscriptionStoreId = "9ND34FHZPLDQ";
    // Preenchido com o ID real do complemento após a criação no Partner Center.
    public const string OneOffStoreId = "9NMJJ4C9MCNC";

    readonly StoreContext context = StoreContext.GetDefault();

    public async Task<StorePurchaseResult> PurchaseSubscriptionAsync()
        => await PurchaseAsync(SubscriptionStoreId, "Assinatura Omnia Metadata Pro");

    public async Task<StorePurchaseResult> PurchaseOneOffAsync()
    {
        if (string.IsNullOrWhiteSpace(OneOffStoreId))
            return new(StorePurchaseStatus.NotPurchased, "OMNIA-QUERY-ONEOFF", "A consulta avulsa ainda não foi habilitada na Microsoft Store.");
        return await PurchaseAsync(OneOffStoreId, "Consulta avulsa");
    }

    public async Task<bool> HasActiveSubscriptionAsync()
    {
        try
        {
            var license = await context.GetAppLicenseAsync();
            return license.AddOnLicenses.ContainsKey(SubscriptionStoreId);
        }
        catch
        {
            // Fora de um pacote MSIX associado à Store, a API não fornece licença.
            return false;
        }
    }

    async Task<StorePurchaseResult> PurchaseAsync(string storeId, string label)
    {
        try
        {
            var result = await context.RequestPurchaseAsync(storeId);
            return new(result.Status, storeId, result.Status switch
            {
                StorePurchaseStatus.Succeeded => $"{label} adquirida. A licença será sincronizada com sua conta.",
                StorePurchaseStatus.AlreadyPurchased => $"{label} já está associada à sua conta Microsoft Store.",
                StorePurchaseStatus.NotPurchased => "A compra não foi concluída.",
                StorePurchaseStatus.NetworkError => "A Microsoft Store não respondeu. Tente novamente quando houver conexão.",
                StorePurchaseStatus.ServerError => "A Microsoft Store informou um erro de servidor. Tente novamente mais tarde.",
                _ => $"A Microsoft Store retornou: {result.Status}."
            });
        }
        catch (Exception ex)
        {
            return new(StorePurchaseStatus.NotPurchased, storeId, $"Não foi possível abrir a compra da Microsoft Store: {ex.Message}");
        }
    }
}
