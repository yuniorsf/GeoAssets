using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Isp.Bss;

/// <summary>Creates a FTTH drop-install <see cref="ServiceOrder"/> from a BSS/OSS-spawned order (XD01-280's AC1).</summary>
public sealed class BssOrderSpawnService(IServiceOrderWriter serviceOrders, TimeProvider timeProvider)
{
    public async Task<ServiceOrder> SpawnFtthDropInstallOrderAsync(
        string providerId, BssOrderSpawnSpec spec, Guid organizationId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(providerId);
        ArgumentNullException.ThrowIfNull(spec);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var order = new ServiceOrder
        {
            OrderTypeId = FtthDropInstallOrderTypes.FtthDropInstallId,
            Title = $"FTTH Drop Install — {spec.SubscriberName}",
            Status = FtthDropInstallStatus.CrmSale,
            CreatedBy = "bss-webhook",
            CreatedAt = now,
            OrganizationId = organizationId,
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [BssOrderAttributes.ProviderIdKey] = providerId,
                [BssOrderAttributes.SubscriberIdKey] = spec.SubscriberId,
                [BssOrderAttributes.SubscriberNameKey] = spec.SubscriberName,
                [BssOrderAttributes.ServiceAddressKey] = spec.ServiceAddress,
                [BssOrderAttributes.PlanIdKey] = spec.PlanId,
            },
        };

        if (spec.ContactPhone is not null)
            order.Attributes[BssOrderAttributes.ContactPhoneKey] = spec.ContactPhone;
        if (spec.ContactEmail is not null)
            order.Attributes[BssOrderAttributes.ContactEmailKey] = spec.ContactEmail;

        await serviceOrders.AddAsync(order, ct).ConfigureAwait(false);
        return order;
    }
}
