using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.NeutralOperator.Server;

/// <summary>
/// This host's <see cref="IAssetSpatialQuery"/> (XD01-382): there is none. Its features live in the kernel, and every
/// selection of an order's targets is resolved there, by the global target ledger (<see cref="LedgerOrderTargetSelectionService"/>).
/// It exists only so the chassis' selection routes, which consult the strategy catalog to word their problems, can be
/// composed here; nothing in this host runs a strategy, and a call that tried would fail loudly rather than select
/// nothing.
/// </summary>
internal sealed class KernelSelectionOnlySpatialQuery : IAssetSpatialQuery
{
    private const string Reason = "GeoAssets.NeutralOperator.Server queries no features itself: selections are resolved by the kernel's global target ledger.";

    public Task<IReadOnlyList<GeoFeature>> FindCoveredByAsync(GeoPolygon area, AssetSpatialFilter filter, CancellationToken ct) =>
        throw new NotSupportedException(Reason);

    public Task<IReadOnlyList<GeoFeature>> FindByAttributeValuesAsync(
        string attributeKey, IReadOnlyCollection<string> values, AssetSpatialFilter filter, CancellationToken ct) =>
        throw new NotSupportedException(Reason);
}
