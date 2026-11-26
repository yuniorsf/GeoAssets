using GeoAssets.Ingestion.Pipeline;
using GeoAssets.Ingestion.Profiles;
using GeoAssets.Ingestion.Sources;

namespace GeoAssets.Ingestion.Resolution;

/// <summary>
/// Pass 1: streams every index layer, maps its records and indexes each element under the layer's
/// <see cref="LayerDefinition.IndexKeys"/> (present, non-zero values) and, for snap-target layers, in the STRtree.
/// Mapping skips are not reported here (pass 2 reports them for the imported layers).
/// </summary>
internal sealed class ReferenceIndexBuilder(IFeatureSource source, ISourceProfile profile)
{
    public async Task<ReferenceIndex> BuildAsync(IReadOnlyList<PlannedLayer> layers, CancellationToken cancellationToken)
    {
        var index = new ReferenceIndex();
        foreach (var layer in layers)
        {
            await IndexLayerAsync(index, layer, cancellationToken).ConfigureAwait(false);
        }

        return index;
    }

    private async Task IndexLayerAsync(ReferenceIndex index, PlannedLayer layer, CancellationToken cancellationToken)
    {
        var mapper = profile.MapperFor(layer.Definition.Name);
        await foreach (var record in source.ReadAsync(layer.FileSet, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Index(index, layer.Definition, record, mapper.Map(record, layer.Scope));
        }
    }

    private static void Index(ReferenceIndex index, LayerDefinition definition, RawRecord record, MappingResult mapping)
    {
        var element = mapping.Element;
        if (element is null || !RepresentativePoint.TryGet(element.Geometry, out var x, out var y))
        {
            return;
        }

        var entry = new IndexEntry(FeatureIdentity.For(element.Key).Value, x, y);
        foreach (var key in definition.IndexKeys)
        {
            var value = IndexKeyValue.From(record.Attributes.GetValueOrDefault(key.Field));
            if (value is not null)
            {
                index.AddKey(new IndexKey(key.Namespace, value), entry);
            }
        }

        if (definition.SnapTarget)
        {
            index.AddSnapTarget(entry);
        }
    }
}
