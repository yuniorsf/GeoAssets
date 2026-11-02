namespace GeoAssets.Workflow.Contractors;

/// <summary>
/// One itemized BoQ line within an <see cref="EspelhoDeMedicaoDossierResult"/> — a
/// <see cref="ContractorUsPosting"/>'s snapshotted figures plus the SHA-256 content hashes of
/// every photo proof claimed under the same <see cref="ContractorUsLedgerEntry.UsCode"/> for the
/// same service order (XD01-249).
/// </summary>
public sealed record EspelhoDeMedicaoItemLine(
    string ItemCode,
    decimal Quantity,
    decimal UnitUs,
    decimal TotalUs,
    decimal MonetaryRate,
    decimal TotalBrl,
    IReadOnlyList<string> EvidencePhotoHashes);
