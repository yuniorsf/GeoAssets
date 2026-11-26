using GeoAssets.Core.Models;
using GeoAssets.Ingestion.Model;

namespace GeoAssets.Ingestion.Pipeline;

/// <summary>
/// Where projected features go (NDJSON file, kernel bulk import…). Chunks are numbered from 0 in a deterministic
/// order so a sink can skip chunks a previous, interrupted run already wrote.
/// </summary>
public interface IFeatureSink
{
    Task EnsureAssetTypesAsync(IReadOnlyList<AssetClassDefinition> classes, CancellationToken cancellationToken);

    /// <summary>The asset-type id features of <paramref name="assetClass"/> get (valid after <see cref="EnsureAssetTypesAsync"/>).</summary>
    string AssetTypeIdFor(string assetClass);

    Task<SinkChunkResult> WriteChunkAsync(int sequence, IReadOnlyList<GeoFeature> features, CancellationToken cancellationToken);

    Task CompleteAsync(CancellationToken cancellationToken);
}
