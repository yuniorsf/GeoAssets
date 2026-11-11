namespace GeoAssets.Shared.Services.FieldWork;

/// <summary>
/// An inspector's completed pole census checklist (XD01-260) — the structured, on-device
/// counterpart of <c>PoleSharingComplianceChecklistSchema</c>'s five required fields, before
/// <see cref="PoleCensusInspectionService.RecordSubmission"/> encodes it onto a
/// <c>ServiceOrder.Attributes</c> dictionary for schema validation and queuing.
/// </summary>
public sealed record PoleCensusSubmission(
    string StructuralCondition,
    bool Band500MmCompliant,
    double GroundToLowestCableMeters,
    double PowerToCommunicationBandMeters,
    IReadOnlyList<PoleCensusOccupantSubmission> Occupants,
    bool UnauthorizedCableDetected);
