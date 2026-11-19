using GeoAssets.Core.Models;

namespace GeoAssets.Workflow.Agents.Dispatch;

/// <summary>
/// The asset reads <see cref="DynamicCrewDispatchAdvisorService"/> needs (XD01-386) — an order's
/// located feature and the routine crew roster. A narrow port rather than the whole
/// <c>IAssetProvider</c>, so a vertical host answers it over the core's API instead of opening the
/// core asset database (Invariant 1, Core Database Encapsulation). An implementation that cannot
/// reach its asset source throws <see cref="Core.Interfaces.GeoCoreUnavailableException"/>.
/// </summary>
public interface IDispatchAssetQuery
{
    /// <summary>The feature with <paramref name="id"/>, or <c>null</c> when it doesn't exist.</summary>
    Task<GeoFeature?> GetFeatureAsync(string id, CancellationToken ct);

    /// <summary>Every feature of <paramref name="assetTypeId"/>.</summary>
    Task<IReadOnlyList<GeoFeature>> GetFeaturesByAssetTypeAsync(string assetTypeId, CancellationToken ct);
}
