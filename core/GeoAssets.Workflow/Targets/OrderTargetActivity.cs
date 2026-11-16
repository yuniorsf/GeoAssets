namespace GeoAssets.Workflow.Targets;

/// <summary>
/// One kind of work an order type allows on each of its targets (e.g. "inspect", "replace").
/// </summary>
/// <param name="Id">Machine-readable identifier, unique within one <see cref="OrderTargetPolicy"/>.</param>
/// <param name="DisplayName">Human-readable label.</param>
public sealed record OrderTargetActivity(string Id, string DisplayName);
