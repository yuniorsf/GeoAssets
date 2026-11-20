using GeoAssets.Core.Interfaces;

namespace GeoAssets.Server;

/// <summary>
/// The applier the kernel's <see cref="AssetMutationBatchApplyHandler"/> applies batches with (XD01-383), and the catalog
/// instance this host is: the provider's registered <see cref="IAssetMutationBatchApplier"/> (e.g. the PostgreSQL pushdown,
/// XD01-374) — or, when none is, <see cref="Core.Services.CatalogMutationBatchApplier"/> over that catalog of the
/// <see cref="ICatalogPool"/> — inside <see cref="ObservableAssetMutationBatchApplier"/>'s span and metrics.
/// </summary>
/// <param name="CatalogInstanceId">The catalog instance this host serves; batches addressed to another are not its to answer.</param>
/// <param name="Applier">The decorated applier.</param>
public sealed record KernelAssetMutationApplier(string CatalogInstanceId, IAssetMutationBatchApplier Applier);
