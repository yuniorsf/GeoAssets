using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.Persistence.Entities;

namespace GeoAssets.Workflow.Persistence;

/// <summary>Converts between the domain <see cref="ContractorUsPosting"/> and the EF entity <see cref="ContractorUsPostingRecord"/>.</summary>
internal static class ContractorUsPostingMapper
{
    public static ContractorUsPostingRecord ToRecord(ContractorUsPosting posting) => new()
    {
        Id = posting.Id,
        ServiceOrderId = posting.ServiceOrderId,
        ContractorOrganizationId = posting.ContractorOrganizationId,
        ItemCode = posting.ItemCode,
        Quantity = posting.Quantity,
        UnitUs = posting.UnitUs,
        TotalUs = posting.TotalUs,
        MonetaryRate = posting.MonetaryRate,
        TotalBrl = posting.TotalBrl,
        PostedAtUtc = posting.PostedAtUtc,
    };

    public static ContractorUsPosting ToDomain(ContractorUsPostingRecord record) => new()
    {
        Id = record.Id,
        ServiceOrderId = record.ServiceOrderId,
        ContractorOrganizationId = record.ContractorOrganizationId,
        ItemCode = record.ItemCode,
        Quantity = record.Quantity,
        UnitUs = record.UnitUs,
        MonetaryRate = record.MonetaryRate,
        PostedAtUtc = record.PostedAtUtc,
    };
}
