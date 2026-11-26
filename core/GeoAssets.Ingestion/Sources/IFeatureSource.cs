namespace GeoAssets.Ingestion.Sources;

/// <summary>Streams the records of one layer file set (never loads the whole layer).</summary>
public interface IFeatureSource
{
    IAsyncEnumerable<RawRecord> ReadAsync(LayerFileSet layer, CancellationToken cancellationToken);
}
