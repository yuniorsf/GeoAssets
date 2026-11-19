namespace GeoAssets.Core.Providers;

/// <summary>Service keys of the organization-scoped catalogs (XD01-338, XD01-373).</summary>
public static class OrganizationCatalogKeys
{
    /// <summary>
    /// The key the host's shared (tenant-less) <see cref="Interfaces.IAssetProvider"/> singleton is re-registered under once
    /// each request's <see cref="Interfaces.IAssetProvider"/> becomes its organization's view.
    /// </summary>
    public const string SharedCatalog = "geoassets:shared-asset-catalog";
}
