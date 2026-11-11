namespace GeoAssets.Shared.Services.FieldWork;

/// <summary>
/// One inspector-observed attachment slot on a pole census visit (XD01-260) — the on-device
/// counterpart of <c>PoleSharingComplianceChecklistSchema</c>'s <c>occupants</c> array entry
/// shape, before it's JSON-encoded into <see cref="PoleCensusSubmission"/>.
/// </summary>
public sealed record PoleCensusOccupantSubmission(
    int SlotIndex,
    string Status,
    string? OperatorCnpj = null);
