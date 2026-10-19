using Windows.Services.Store;

namespace Omnia.Metadata.App;

public sealed record StorePurchaseResult(StorePurchaseStatus Status, string ProductId, string Message);

public sealed class StoreBillingClient
{
    public const string SubscriptionStoreId = "9ND34FHZPLDQ";
    public const string OneOffStoreId = "9NMJJ4C9MCNC";

    readonly StoreContext context;
    readonly bool windowReady;

    public StoreBillingClient(Control owner)
    {
        context = StoreContext.GetDefault();
        try
        {
            if (!owner.IsHandleCreated) owner.CreateControl();
            WinRT.Interop.InitializeWithWindow.Initialize(context, owner.Handle);
            windowReady = owner.Handle != IntPtr.Zero;
        }
        catch
        {
            windowReady = false;
        }
    }

    public Task<StorePurchaseResult> PurchaseSubscriptionAsync() =>
        PurchaseAsync(SubscriptionStoreId, "Assinatura Omnia Metadata Pro");

    public Task<StorePurchaseResult> PurchaseOneOffAsync() =>
        PurchaseAsync(OneOffStoreId, "Consulta avulsa");

    public async Task<bool> HasActiveSubscriptionAsync()
    {
        try
        {
            var license = await context.GetAppLicenseAsync();
            return license.AddOnLicenses.ContainsKey(SubscriptionStoreId);
        }
        catch
        {
            return false;
        }
    }

    public async Task<int> GetOneOffBalanceAsync()
    {
        if (!windowReady || string.IsNullOrWhiteSpace(OneOffStoreId)) return 0;
        try
        {
            var result = await context.GetConsumableBalanceRemainingAsync(OneOffStoreId);
            return checked((int)result.BalanceRemaining);
        }
        catch
        {
            return 0;
        }
    }

    public async Task<bool> ConsumeOneOffAsync()
    {
        if (!windowReady || string.IsNullOrWhiteSpace(OneOffStoreId)) return false;
        try
        {
            var result = await context.ReportConsumableFulfillmentAsync(OneOffStoreId, 1, Guid.NewGuid());
            return string.Equals(result.Status.ToString(), "Succeeded", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    async Task<StorePurchaseResult> PurchaseAsync(string storeId, string label)
    {
        if (!windowReady)
            return new(StorePurchaseStatus.NotPurchased, storeId, "A Microsoft Store não conseguiu associar a compra à janela do aplicativo. Feche e abra a versão instalada pela Microsoft Store.");

        try
        {
            var result = await context.RequestPurchaseAsync(storeId);
            return new(result.Status, storeId, result.Status switch
            {
                StorePurchaseStatus.Succeeded => $"{label} adquirida. A licença foi sincronizada com a Microsoft Store.",
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
