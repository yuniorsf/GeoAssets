using System.Globalization;
using System.Runtime.InteropServices;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using NetTopologySuite.Geometries;

namespace GeoAssets.Provider.PMTiles.Mvt;

/// <summary>
/// Turns a decoded vector tile feature into a <see cref="GeoFeature"/> the map's vector path draws (XD01-438): its tile
/// coordinates are placed on the tile's Web Mercator envelope and unprojected to EPSG:4326, longitude first (rounded to
/// 7 decimals, about a centimetre). Points become <see cref="GeoPoint"/>/MultiPoint, lines LineString/MultiLineString,
/// polygons Polygon/MultiPolygon — a ring with the winding of the feature's first ring is an exterior ring, one with the
/// opposite winding a hole of the exterior before it (MVT 2.1 §4.3.4.4). A point in the tile's buffer (outside
/// <c>0 … extent</c>) is dropped, since the neighbouring tile holds it; lines and polygons keep their buffer, so a shape
/// crossing tiles is drawn as one piece per tile. Unknown geometry, and geometry left empty, yields no feature.
/// </summary>
internal static class MvtFeatureProjector
{
    /// <summary>The custom attribute naming the vector tile layer a feature came from.</summary>
    public const string LayerAttribute = "mvt:layer";

    private const int CoordinateDecimals = 7;

    /// <summary>
    /// The feature <paramref name="feature"/> of <paramref name="layer"/> in <paramref name="tile"/> as a
    /// <see cref="GeoFeature"/> with id <paramref name="id"/>, or <see langword="null"/> when it has no drawable geometry.
    /// </summary>
    public static GeoFeature? ToGeoFeature(MvtFeature feature, MvtLayer layer, TileAddress tile, string id)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(layer);

        var placement = new Placement(tile.Envelope3857, layer.Extent);
        Geometry? geometry = feature.GeometryType switch
        {
            MvtGeometryType.Point => Points(feature, layer.Extent, placement),
            MvtGeometryType.LineString => Lines(feature, placement),
            MvtGeometryType.Polygon => Polygons(feature, placement),
            _ => null,
        };
        if (geometry is null || geometry.IsEmpty)
            return null;

        var properties = new GeoFeatureProperties
        {
            Name = feature.Properties.GetValueOrDefault("name", string.Empty),
            AssetTypeId = AssetTypeOf(feature.GeometryType).Id.ToString(),
        };
        properties.CustomAttributes[LayerAttribute] = layer.Name;
        foreach (var (key, value) in feature.Properties)
            properties.CustomAttributes.TryAdd(key, value);
        if (feature.Id is { } mvtId)
            properties.CustomAttributes.TryAdd("mvt:id", mvtId.ToString(CultureInfo.InvariantCulture));

        return new GeoFeature { Id = id, Geometry = GeoGeometry.FromNts(geometry), Properties = properties };
    }

    private static AssetType AssetTypeOf(MvtGeometryType type) => type switch
    {
        MvtGeometryType.Point => AssetType.Point,
        MvtGeometryType.LineString => AssetType.Line,
        _ => AssetType.Area,
    };

    private static Geometry? Points(MvtFeature feature, int extent, Placement placement)
    {
        var points = feature.Parts
            .SelectMany(part => part)
            .Where(p => p.X >= 0 && p.X < extent && p.Y >= 0 && p.Y < extent)
            .Select(p => GeoFactory.Wgs84.CreatePoint(placement.Place(p)))
            .ToArray();

        return points.Length switch
        {
            0 => null,
            1 => points[0],
            _ => GeoFactory.Wgs84.CreateMultiPoint(points),
        };
    }

    private static Geometry? Lines(MvtFeature feature, Placement placement)
    {
        var lines = feature.Parts
            .Where(part => part.Count >= 2)
            .Select(part => GeoFactory.Wgs84.CreateLineString([.. part.Select(placement.Place)]))
            .ToArray();

        return lines.Length switch
        {
            0 => null,
            1 => lines[0],
            _ => GeoFactory.Wgs84.CreateMultiLineString(lines),
        };
    }

    private static Geometry? Polygons(MvtFeature feature, Placement placement)
    {
        var rings = feature.Parts.Where(part => part.Count >= 3).Select(part => (Part: part, Area: SignedArea(part))).Where(r => r.Area != 0).ToArray();
        if (rings.Length == 0)
            return null;

        var exteriorSign = Math.Sign(rings[0].Area);
        var polygons = new List<Polygon>();
        LinearRing? shell = null;
        var holes = new List<LinearRing>();
        foreach (var (part, area) in rings)
        {
            var ring = Ring(part, placement);
            if (Math.Sign(area) == exteriorSign)
            {
                if (shell is not null)
                    polygons.Add(GeoFactory.Wgs84.CreatePolygon(shell, [.. holes]));
                shell = ring;
                holes.Clear();
            }
            else
            {
                holes.Add(ring);
            }
        }

        polygons.Add(GeoFactory.Wgs84.CreatePolygon(shell!, [.. holes]));
        return polygons.Count == 1 ? polygons[0] : GeoFactory.Wgs84.CreateMultiPolygon([.. polygons]);
    }

    private static LinearRing Ring(IReadOnlyList<MvtPoint> part, Placement placement)
    {
        var coordinates = new Coordinate[part.Count + 1];
        for (var i = 0; i < part.Count; i++)
            coordinates[i] = placement.Place(part[i]);
        coordinates[^1] = coordinates[0].Copy();
        return GeoFactory.Wgs84.CreateLinearRing(coordinates);
    }

    /// <summary>Twice the ring's signed area in tile coordinates (the surveyor's formula; the ring is implicitly closed).</summary>
    private static long SignedArea(IReadOnlyList<MvtPoint> ring)
    {
        long sum = 0;
        for (var i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            sum += ((long)a.X * b.Y) - ((long)b.X * a.Y);
        }

        return sum;
    }

    /// <summary>Where a tile coordinate falls on the map: the tile's Web Mercator envelope, divided into extent units.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly struct Placement(MercatorBounds envelope, int extent)
    {
        private readonly double _scaleX = envelope.Width / extent;
        private readonly double _scaleY = envelope.Height / extent;

        public Coordinate Place(MvtPoint point)
        {
            var (longitude, latitude) = WebMercator.Unproject(
                envelope.MinX + (point.X * _scaleX),
                envelope.MaxY - (point.Y * _scaleY));
            return new Coordinate(Math.Round(longitude, CoordinateDecimals), Math.Round(latitude, CoordinateDecimals));
        }
    }
}
