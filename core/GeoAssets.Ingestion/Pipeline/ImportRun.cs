using GeoAssets.Ingestion.Projection;
using GeoAssets.Ingestion.Resolution;
using GeoAssets.Ingestion.Sources;
using GeoAssets.Ingestion.Spatial;
using GeoAssets.Ingestion.Validation;

namespace GeoAssets.Ingestion.Pipeline;

/// <summary>
/// The state and steps of one <see cref="ImportPipeline"/> run: inventory issues, pass 1 (reference index), asset
/// types, pass 2 (stream, process, chunk), completion and the report.
/// </summary>
internal sealed class ImportRun(IFeatureSource source, ICoordinateTransformerFactory transformerFactory, ImportRequest request, IFeatureSink sink)
{
    private readonly IssueLog _issues = new();
    private readonly ImportStatistics _statistics = new();
    private readonly TransformerCache _transformers = new(transformerFactory);

    public async Task<ImportReport> ExecuteAsync(CancellationToken cancellationToken)
    {
        _issues.AddRange(request.Inventory.Issues);
        var index = await new ReferenceIndexBuilder(source, request.Profile)
            .BuildAsync(LayerPlanner.IndexLayers(request), cancellationToken)
            .ConfigureAwait(false);
        await sink.EnsureAssetTypesAsync(request.Profile.AssetClasses, cancellationToken).ConfigureAwait(false);

        var registry = new ElementRegistry();
        var topology = new TopologyAssembler(registry, _issues);
        var processor = CreateProcessor(index, registry, topology);
        var chunks = new ChunkWriter(sink, request.ChunkSize, _issues);
        foreach (var plan in LayerPlanner.ImportLayers(request))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_transformers.TryAcquire(plan.FileSet, _issues, out var transformer))
            {
                var layer = new ActiveLayer(plan, request.Profile.MapperFor(plan.Definition.Name), transformer);
                await ImportLayerAsync(layer, processor, chunks, cancellationToken).ConfigureAwait(false);
            }
        }

        await chunks.FlushAsync(cancellationToken).ConfigureAwait(false);
        topology.ReportUnattached();
        await sink.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return ImportReportBuilder.Build(request, _statistics, _issues, chunks, _transformers.PositionAccuracy);
    }

    private async Task ImportLayerAsync(ActiveLayer layer, ElementProcessor processor, ChunkWriter chunks, CancellationToken cancellationToken)
    {
        await foreach (var record in source.ReadAsync(layer.Plan.FileSet, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var feature = processor.Process(record, layer);
            if (feature is not null)
            {
                await chunks.AddAsync(feature, layer.Plan.Scope, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private ElementProcessor CreateProcessor(ReferenceIndex index, ElementRegistry registry, TopologyAssembler topology) =>
        new(
            registry,
            new ElementShapeValidator(request.Profile.AssetClasses),
            new ElementReferenceResolver(ReferenceResolverChain.KeyThenSnap(index), _statistics, _issues),
            [new EndpointDistanceRule(index), new SelfLoopRule()],
            topology,
            new GeoFeatureProjector(request.Profile.Id, request.OrganizationId, sink),
            _statistics,
            _issues);
}
