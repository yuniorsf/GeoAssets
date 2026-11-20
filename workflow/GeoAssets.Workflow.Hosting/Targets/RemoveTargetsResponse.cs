namespace GeoAssets.Workflow.Hosting.Targets;

/// <summary>How many of the listed targets were removed; ids the order did not hold are not counted.</summary>
public sealed record RemoveTargetsResponse(int Removed);
