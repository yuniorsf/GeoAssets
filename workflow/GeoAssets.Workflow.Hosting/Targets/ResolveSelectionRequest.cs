namespace GeoAssets.Workflow.Hosting.Targets;

/// <summary>Body of <c>POST /selection/resolve</c>: a map gesture expressed as a selection strategy run.</summary>
public sealed record ResolveSelectionRequest(string? StrategyId, Dictionary<string, object>? Parameters);
