namespace GeoAssets.Provider.PMTiles.Mvt;

/// <summary>
/// One decoded vector tile feature (XD01-438): its optional id, geometry type, properties (every value as invariant
/// text) and geometry parts in tile coordinates — a point per part for points, a line per part for line strings, a
/// ring per part (not repeating its first vertex) for polygons.
/// </summary>
internal sealed record MvtFeature(
    ulong? Id,
    MvtGeometryType GeometryType,
    IReadOnlyDictionary<string, string> Properties,
    IReadOnlyList<IReadOnlyList<MvtPoint>> Parts);
