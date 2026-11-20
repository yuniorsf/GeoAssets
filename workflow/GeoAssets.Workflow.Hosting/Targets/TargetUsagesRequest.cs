namespace GeoAssets.Workflow.Hosting.Targets;

/// <summary>
/// Body of <c>POST /service-orders/targets/usages</c>: 1 to 500 feature ids, and the order the
/// caller is working on (its own targets are never reported).
/// </summary>
public sealed record TargetUsagesRequest(
    IReadOnlyList<string>? FeatureIds,
    string? ExcludingOrderId,
    int Page = 1,
    int PageSize = 50);
