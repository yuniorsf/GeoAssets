using NetTopologySuite.Geometries;

namespace GeoAssets.Ingestion.Sources;

/// <summary>
/// One source record as read: native-CRS geometry and attribute values keyed by full field name (falling back to the
/// table's own name). Empty when the layer is <see cref="LayerVerdict.GeometryOnly"/>.
/// </summary>
public sealed record RawRecord(
    LayerFileSet Layer,
    long RecordNumber,
    Geometry Geometry,
    IReadOnlyDictionary<string, object?> Attributes);
