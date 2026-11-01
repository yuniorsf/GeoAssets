namespace GeoAssets.Workflow.Contractors;

/// <summary>Builds an immutable <see cref="ContractorUsPosting"/> snapshotted from a <see cref="UsCatalogItem"/> at the moment of posting.</summary>
public static class ContractorUsPostingFactory
{
    public static ContractorUsPosting Post(
        string serviceOrderId,
        Guid contractorOrganizationId,
        UsCatalogItem catalogItem,
        decimal quantity,
        TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceOrderId);
        ArgumentNullException.ThrowIfNull(catalogItem);
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new ContractorUsPosting
        {
            ServiceOrderId = serviceOrderId,
            ContractorOrganizationId = contractorOrganizationId,
            ItemCode = catalogItem.ItemCode,
            Quantity = quantity,
            UnitUs = catalogItem.BaseUsFactor * catalogItem.RegionalMultiplier,
            MonetaryRate = catalogItem.MonetaryConversionRate,
            PostedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
        };
    }
}
