namespace GeoAssets.Server.Targets;

/// <summary>Body of <c>POST …/targets/remove</c>: 1 to 500 target ids of that order.</summary>
public sealed record RemoveTargetsRequest(IReadOnlyList<string>? TargetIds);
