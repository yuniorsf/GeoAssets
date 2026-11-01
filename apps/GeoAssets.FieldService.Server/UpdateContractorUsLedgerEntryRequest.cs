namespace GeoAssets.FieldService.Server;

/// <summary>Request body for replacing a ledger entry's editable fields.</summary>
internal sealed record UpdateContractorUsLedgerEntryRequest(
    string? ServiceOrderId,
    string UsCode,
    string Description,
    decimal Quantity,
    decimal UnitValue,
    List<PhotoProofDto> PhotoProof);
