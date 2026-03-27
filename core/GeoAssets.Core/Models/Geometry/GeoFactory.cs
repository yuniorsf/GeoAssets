using System.Collections.Concurrent;
using NtsGeometryFactory = NetTopologySuite.Geometries.GeometryFactory;
using NtsPrecisionModel   = NetTopologySuite.Geometries.PrecisionModel;

namespace GeoAssets.Core.Models.Geometry;

/// <summary>
/// Shared pool of <see cref="NtsGeometryFactory"/> instances keyed by SRID.
///
/// Re-using factory instances avoids repeated allocations and lets NTS share
/// internal precision-model state across geometries with the same SRID.
/// </summary>
public static class GeoFactory
{
    private static readonly ConcurrentDictionary<int, NtsGeometryFactory> _pool = new();

    /// <summary>Returns (or creates) the factory for <paramref name="srid"/>.</summary>
    public static NtsGeometryFactory For(int srid) =>
        _pool.GetOrAdd(srid, static id => new NtsGeometryFactory(new NtsPrecisionModel(), id));

    /// <summary>WGS-84 / EPSG:4326 factory — default for GeoJSON (RFC 7946).</summary>
    public static NtsGeometryFactory Wgs84 => For(4326);

    /// <summary>Web Mercator / EPSG:3857 factory — common for tile-based maps.</summary>
    public static NtsGeometryFactory WebMercator => For(3857);
}
