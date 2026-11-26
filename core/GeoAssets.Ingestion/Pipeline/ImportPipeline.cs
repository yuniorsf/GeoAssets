using GeoAssets.Ingestion.Sources;
using GeoAssets.Ingestion.Spatial;

namespace GeoAssets.Ingestion.Pipeline;

/// <summary>
/// The distributor-network import engine (see <see cref="IImportPipeline"/>): reads layers through
/// <paramref name="source"/> and reprojects with one transformer per CRS from <paramref name="transformers"/>.
/// Each <see cref="RunAsync"/> is an independent run.
/// </summary>
public sealed class ImportPipeline(IFeatureSource source, ICoordinateTransformerFactory transformers) : IImportPipeline
{
    public Task<ImportReport> RunAsync(ImportRequest request, IFeatureSink sink, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sink);
        return new ImportRun(source, transformers, request, sink).ExecuteAsync(cancellationToken);
    }
}
