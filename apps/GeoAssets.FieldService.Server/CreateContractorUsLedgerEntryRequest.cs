namespace GeoAssets.FieldService.Server;

/// <summary>Request body for creating a ledger entry. <c>OrganizationId</c> is deliberately absent — it's always the caller's own tenant (<see cref="GeoAssets.Core.Interfaces.ITenantContext"/>), never client-supplied.</summary>
internal sealed record CreateContractorUsLedgerEntryRequest(
    string? ServiceOrderId,
    string UsCode,
    string Description,
    decimal Quantity,
    decimal UnitValue,
    List<PhotoProofDto> PhotoProof);
