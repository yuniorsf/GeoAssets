using GeoAssets.Core.Isp.NeutralOperator;

namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>
/// One pole's slot to release when a <c>CUT_AND_REMOVE_OS</c> order completes (XD01-264).
/// <see cref="PoleAttachmentSlotMatrix"/> has no persistence/repository layer yet (the same
/// pre-existing gap XD01-259/266/271 each independently flagged) — the caller is responsible
/// for loading the real matrix for <see cref="PoleFeatureId"/> and persisting it back after
/// <see cref="CleanupDriveBatchingService.CompleteCutOrderAsync"/> mutates it in place.
/// </summary>
public sealed record PoleSlotRelease(
    string PoleFeatureId,
    PoleAttachmentSlotMatrix Matrix,
    int SlotIndex);
