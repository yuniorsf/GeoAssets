namespace GeoAssets.Core.Models;

/// <summary>Narrowing and bounding criteria of an <see cref="Interfaces.IReferenceFeatureQuery"/> read (XD01-330).</summary>
/// <param name="AssetTypeId">
/// When set, only features of this asset type (<see cref="GeoFeatureProperties.AssetTypeId"/>, ordinal); <c>null</c> or
/// empty applies no type filter.
/// </param>
/// <param name="ScopeIds">
/// <c>null</c>: every feature of the organization. Otherwise only the features whose scope (the <c>scopeId</c> custom
/// attribute, <see cref="Interfaces.IScopeableResource.ScopeId"/>) is one of these, compared exactly — the scopes of the
/// grants the caller reaches the organization through. An empty set matches nothing, nor does a feature without a scope.
/// </param>
/// <param name="MaxResults">
/// Upper bound on the returned list; must be greater than zero. The cut is applied after ordering by feature id (ordinal,
/// ascending), so a truncated result is deterministic.
/// </param>
public sealed record ReferenceFeatureFilter(
    string? AssetTypeId,
    IReadOnlyCollection<string>? ScopeIds,
    int MaxResults);
