using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.Persistence.Entities;

namespace GeoAssets.Workflow.Persistence;

/// <summary>Converts between the domain <see cref="UsCatalogItem"/> and the EF entity <see cref="UsCatalogItemRecord"/>.</summary>
internal static class UsCatalogItemMapper
{
    public static UsCatalogItemRecord ToRecord(UsCatalogItem item) => new()
    {
        Id = item.Id,
        OrganizationId = item.OrganizationId,
        ItemCode = item.ItemCode,
        Description = item.Description,
        Unit = item.Unit,
        BaseUsFactor = item.BaseUsFactor,
        RegionalMultiplier = item.RegionalMultiplier,
        MonetaryConversionRate = item.MonetaryConversionRate,
        EffectiveFrom = item.EffectiveFrom,
        EffectiveTo = item.EffectiveTo,
    };

    public static UsCatalogItem ToDomain(UsCatalogItemRecord record) => new()
    {
        Id = record.Id,
        OrganizationId = record.OrganizationId,
        ItemCode = record.ItemCode,
        Description = record.Description,
        Unit = record.Unit,
        BaseUsFactor = record.BaseUsFactor,
        RegionalMultiplier = record.RegionalMultiplier,
        MonetaryConversionRate = record.MonetaryConversionRate,
        EffectiveFrom = record.EffectiveFrom,
        EffectiveTo = record.EffectiveTo,
    };
}
