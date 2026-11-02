namespace GeoAssets.Workflow.Isp.Bss;

/// <summary>
/// <see cref="Orders.ServiceOrder.Attributes"/> keys a BSS/OSS-spawned FTTH drop-install order
/// carries so <see cref="FtthDropInstallActivationDispatchService"/> can resolve which provider
/// (and which of that provider's own subscriber/contract records) to notify back on activation,
/// without a separate tracking table.
/// </summary>
public static class BssOrderAttributes
{
    /// <summary>The <see cref="IBssOssOrderAdapter.ProviderId"/> that spawned this order.</summary>
    public const string ProviderIdKey = "bssProviderId";

    /// <summary>The provider's own subscriber/contract identifier, round-tripped back on activation.</summary>
    public const string SubscriberIdKey = "bssSubscriberId";

    public const string SubscriberNameKey = "bssSubscriberName";

    public const string ServiceAddressKey = "bssServiceAddress";

    public const string PlanIdKey = "bssPlanId";

    public const string ContactPhoneKey = "bssContactPhone";

    public const string ContactEmailKey = "bssContactEmail";
}
