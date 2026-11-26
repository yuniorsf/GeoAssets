namespace GeoAssets.Ingestion.Pipeline;

/// <summary>
/// Runs the two-pass pipeline: pass 1 indexes every usable layer's <see cref="Profiles.LayerDefinition.IndexKeys"/>
/// (and snap targets, group-anchor candidates and group nodes); pass 2 streams the selected layers in
/// <see cref="Profiles.LayerDefinition.ProcessingOrder"/>, maps, de-duplicates, resolves references (key, then snap;
/// group-anchor references by anchor) in native CRS, validates, reprojects, projects to
/// <c>GeoFeature</c> and writes chunks to the sink.
/// </summary>
public interface IImportPipeline
{
    Task<ImportReport> RunAsync(ImportRequest request, IFeatureSink sink, CancellationToken cancellationToken);
}
