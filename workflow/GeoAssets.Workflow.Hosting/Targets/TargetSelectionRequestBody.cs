namespace GeoAssets.Workflow.Hosting.Targets;

/// <summary>
/// Body of the selection preview and apply routes. The order comes from the route and the
/// caller from the token; neither is accepted here.
/// </summary>
public sealed record TargetSelectionRequestBody(
    string? StrategyId,
    Dictionary<string, object>? Parameters,
    int Page = 1,
    int PageSize = 50,
    bool AcceptTruncation = false);
