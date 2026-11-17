using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging.Events;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// Reloads the batch's organization's cached catalog in <see cref="PostgresOrganizationAssetCatalogs"/>
/// after <paramref name="inner"/> applied the batch (XD01-338). <see cref="PostgresAssetMutationBatchApplier"/>
/// writes through a provider of its own, so without this the organization's catalog would keep
/// serving the features as they were before the batch until something else reloaded it.
///
/// A failed reload fails the call after the batch committed; whoever delivered the batch retries,
/// the applier finds the batch already applied and the reload runs again.
/// </summary>
public sealed class OrganizationCatalogRefreshingApplier(
    IAssetMutationBatchApplier inner, PostgresOrganizationAssetCatalogs catalogs) : IAssetMutationBatchApplier
{
    private readonly IAssetMutationBatchApplier _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly PostgresOrganizationAssetCatalogs _catalogs = catalogs ?? throw new ArgumentNullException(nameof(catalogs));

    public async Task<AssetBatchAppliedEvent> ApplyAsync(AssetMutationBatchRequestedEvent batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var applied = await _inner.ApplyAsync(batch, ct).ConfigureAwait(false);
        await _catalogs.RefreshAsync(batch.OrganizationId, ct).ConfigureAwait(false);
        return applied;
    }
}
