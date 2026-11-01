using GeoAssets.Workflow.Contractors;

namespace GeoAssets.FieldService.Server;

internal sealed record ContractorUsLedgerEntryResponse(
    Guid Id,
    Guid OrganizationId,
    string? ServiceOrderId,
    string UsCode,
    string Description,
    decimal Quantity,
    decimal UnitValue,
    decimal TotalValue,
    List<PhotoProofDto> PhotoProof,
    DateTime CreatedAt,
    DateTime? UpdatedAt)
{
    public static ContractorUsLedgerEntryResponse FromDomain(ContractorUsLedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new(
            entry.Id,
            entry.OrganizationId,
            entry.ServiceOrderId,
            entry.UsCode,
            entry.Description,
            entry.Quantity,
            entry.UnitValue,
            entry.TotalValue,
            entry.PhotoProof.Select(PhotoProofDto.FromDomain).ToList(),
            entry.CreatedAt,
            entry.UpdatedAt);
    }
}
