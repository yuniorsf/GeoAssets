namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>One street-corridor batch's generated, dispatched <c>CUT_AND_REMOVE_OS</c> order (XD01-264).</summary>
public sealed record CutAndRemoveBatchResult(
    string ServiceOrderId,
    IReadOnlyList<string> PoleFeatureIds,
    IReadOnlyList<string> SourceClandestineNoticeOrderIds,
    DateTimeOffset DispatchedAtUtc);
