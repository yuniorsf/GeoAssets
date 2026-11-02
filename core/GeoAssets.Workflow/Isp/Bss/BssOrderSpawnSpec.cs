namespace GeoAssets.Workflow.Isp.Bss;

/// <summary>
/// Normalized fields <see cref="IBssOssOrderAdapter.ParseOrderSpawn"/> extracts from a
/// provider-specific order-spawn webhook payload — enough to create a FTTH drop-install
/// <see cref="Orders.ServiceOrder"/> without the rest of this codebase needing to know any given
/// BSS/OSS vendor's own field names.
/// </summary>
public sealed record BssOrderSpawnSpec(
    string SubscriberId,
    string SubscriberName,
    string ServiceAddress,
    string PlanId,
    string? ContactPhone,
    string? ContactEmail);
