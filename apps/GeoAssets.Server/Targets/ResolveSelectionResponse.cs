namespace GeoAssets.Server.Targets;

/// <summary>
/// The ids of the caller's organization's features a map gesture selects, ordered by id
/// (ordinal). Bounded, not paged: a gesture selecting more than the host allows is rejected.
/// </summary>
public sealed record ResolveSelectionResponse(IReadOnlyList<string> FeatureIds, int Count);
