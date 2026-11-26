using System.Collections;
using System.Runtime.CompilerServices;
using GeoAssets.Ingestion.Sources;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace GeoAssets.Ingestion.Shapefile;

/// <summary>
/// Streams a layer's records with <c>NetTopologySuite.IO.ShapeFile</c>: the <c>.shp</c> shapes in lockstep with the
/// <see cref="LayerFileSet.AttributeTablePath"/> rows (which may be a paired orphan table), decoded with
/// <see cref="LayerFileSet.Encoding"/>; geometry only when the layer has no table. Attributes are keyed by
/// <see cref="LayerFileSet.FieldAliases"/> (falling back to the table's own field name). Null or empty shapes are
/// skipped, but still count for the 1-based <see cref="RawRecord.RecordNumber"/>. Geometries are in the native CRS
/// (SRID 0).
/// </summary>
public sealed class ShapefileFeatureSource : IFeatureSource
{
    private static readonly IReadOnlyDictionary<string, object?> NoAttributes = new Dictionary<string, object?>(StringComparer.Ordinal);

    public async IAsyncEnumerable<RawRecord> ReadAsync(LayerFileSet layer, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (layer.GeometryPath is null)
            yield break;

        await Task.Yield();
        var factory = new GeometryFactory();
        var shapes = new ShapefileReader(layer.GeometryPath, factory).GetEnumerator();
        var table = layer.AttributeTablePath is null ? null : new DbaseFileReader(layer.AttributeTablePath, ShapefileEncodings.Resolve(layer.Encoding));
        var fields = table?.GetHeader().Fields ?? [];
        var keys = fields.Select(field => layer.FieldAliases.GetValueOrDefault(field.Name) ?? field.Name).ToArray();
        var rows = table?.GetEnumerator();
        try
        {
            long recordNumber = 0;
            while (shapes.MoveNext())
            {
                cancellationToken.ThrowIfCancellationRequested();
                recordNumber++;
                var row = rows is not null && rows.MoveNext() ? rows.Current as IList : null;
                if (shapes.Current is not Geometry geometry || geometry.IsEmpty)
                    continue;

                yield return new RawRecord(layer, recordNumber, geometry, row is null ? NoAttributes : ToAttributes(row, fields, keys));
            }
        }
        finally
        {
            (shapes as IDisposable)?.Dispose();
            (rows as IDisposable)?.Dispose();
        }
    }

    private static Dictionary<string, object?> ToAttributes(IList row, DbaseFieldDescriptor[] fields, string[] keys)
    {
        var attributes = new Dictionary<string, object?>(keys.Length, StringComparer.Ordinal);
        for (var i = 0; i < fields.Length && i < row.Count; i++)
            attributes[keys[i]] = DbaseValues.Normalize(row[i], fields[i]);
        return attributes;
    }
}
