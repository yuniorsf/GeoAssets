using GeoAssets.Core.Models;

namespace GeoAssets.Integrations.Scada;

/// <summary>
/// The asset reads <see cref="FeederOutageDispatchService"/> needs (XD01-386) — the tripped
/// switch, its downstream segment, and the crew units to dispatch from. A narrow port rather than
/// the whole <c>IAssetProvider</c>, so a vertical host answers it over the core's API instead of
/// opening the core asset database (Invariant 1, Core Database Encapsulation). An implementation
/// that cannot reach its asset source throws <see cref="Core.Interfaces.GeoCoreUnavailableException"/>.
/// </summary>
public interface IAssetTopologyQuery
{
    /// <summary>The feature with <paramref name="id"/>, or <c>null</c> when it doesn't exist.</summary>
    Task<GeoFeature?> GetFeatureAsync(string id, CancellationToken ct);

    /// <summary>
    /// Every feature reachable downstream of <paramref name="id"/> over edges of
    /// <paramref name="edgeKind"/> (breadth-first, the starting feature excluded unless a cycle
    /// leads back to it) — the same set <c>TopoGraph.GetDescendants</c> returns.
    /// </summary>
    Task<IReadOnlyList<GeoFeature>> GetDescendantsAsync(string id, string edgeKind, CancellationToken ct);

    /// <summary>
    /// Every feature of <paramref name="assetTypeId"/>. Used for the crew lookup: the core has no
    /// spatial-query endpoint, so the nearest-crew ranking happens on the caller's side over the
    /// (small) set of crew units.
    /// </summary>
    Task<IReadOnlyList<GeoFeature>> GetFeaturesByAssetTypeAsync(string assetTypeId, CancellationToken ct);
}
