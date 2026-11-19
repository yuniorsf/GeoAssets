namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Organization-scoped asset catalogs (XD01-373, ADR-001 Phase 4): the catalog as one organization sees it — its own
/// features plus the shared (<see cref="Guid.Empty"/>) ones, never another organization's — whatever provider backs it.
/// A provider plugin may register a native implementation (e.g. one cache per organization); otherwise the host uses
/// <see cref="Providers.FilteringOrganizationCatalogFactory"/>, a filtering view over its one catalog.
/// </summary>
public interface IOrganizationCatalogFactory
{
    /// <summary>
    /// The catalog as <paramref name="organizationId"/> sees it; for <see cref="Guid.Empty"/>, the shared features alone.
    /// It may not be loaded yet: its first synchronous read may then load it.
    /// </summary>
    IAssetProvider GetForOrganization(Guid organizationId);

    /// <summary><see cref="GetForOrganization"/>, loaded before it is returned.</summary>
    ValueTask<IAssetProvider> GetForOrganizationAsync(Guid organizationId, CancellationToken ct);
}
