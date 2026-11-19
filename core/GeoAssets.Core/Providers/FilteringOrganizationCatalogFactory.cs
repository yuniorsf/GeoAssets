using System.Collections.Concurrent;
using GeoAssets.Core.Interfaces;

namespace GeoAssets.Core.Providers;

/// <summary>
/// The generic <see cref="IOrganizationCatalogFactory"/> (XD01-373): for a provider that offers no native organization
/// scoping, each organization's catalog is an <see cref="OrganizationFilteredAssetProvider"/> over the one shared
/// catalog holding every organization's features. Views are created once per organization and kept; they own nothing.
/// </summary>
public sealed class FilteringOrganizationCatalogFactory(IAssetProvider catalog) : IOrganizationCatalogFactory
{
    private readonly ConcurrentDictionary<Guid, OrganizationFilteredAssetProvider> _views = new();

    public IAssetProvider GetForOrganization(Guid organizationId) =>
        _views.GetOrAdd(organizationId, id => new OrganizationFilteredAssetProvider(id, catalog));

    /// <summary>The view is over the already-loaded catalog, so there is nothing to load.</summary>
    public ValueTask<IAssetProvider> GetForOrganizationAsync(Guid organizationId, CancellationToken ct) =>
        ValueTask.FromResult(GetForOrganization(organizationId));
}
