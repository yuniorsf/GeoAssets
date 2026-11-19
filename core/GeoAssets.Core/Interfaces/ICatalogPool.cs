using System.Diagnostics.CodeAnalysis;

namespace GeoAssets.Core.Interfaces;

/// <summary>
/// The server's named asset catalogs (XD01-371, ADR-001 Phase 4): each catalog id — the
/// <c>CatalogInstanceId</c> asset mutation batches are addressed to — resolves to one
/// <see cref="IAssetProvider"/>, built at startup by a provider plugin from configuration.
/// Server-side and not UI-shaped, unlike the client's <see cref="IProviderPool"/>: no active
/// entry, no connect/disconnect. Every catalog is validated on write.
/// </summary>
public interface ICatalogPool
{
    /// <summary>The id of the catalog a request's <see cref="IAssetProvider"/> resolves to.</summary>
    string DefaultCatalogId { get; }

    /// <summary>The ids of every catalog the pool holds, in configuration order.</summary>
    IReadOnlyList<string> CatalogIds { get; }

    /// <summary>The <see cref="DefaultCatalogId"/> catalog.</summary>
    /// <exception cref="KeyNotFoundException">No default catalog is configured.</exception>
    IAssetProvider DefaultCatalog { get; }

    /// <summary>The catalog named <paramref name="catalogId"/> (case-insensitive).</summary>
    /// <exception cref="KeyNotFoundException">No catalog has that id.</exception>
    IAssetProvider GetCatalog(string catalogId);

    /// <summary>The catalog named <paramref name="catalogId"/> (case-insensitive), when there is one.</summary>
    bool TryGetCatalog(string catalogId, [NotNullWhen(true)] out IAssetProvider? catalog);

    /// <summary>
    /// An optional capability port of a catalog — e.g. a pushdown query the concrete provider
    /// implements — or <c>null</c> when the catalog's provider does not offer it. Looked up on the
    /// catalog and on the concrete provider beneath its decorators.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No catalog has that id.</exception>
    TCapability? GetCapability<TCapability>(string catalogId) where TCapability : class;
}
