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
/// crossing tiles is one piece per tile here. Unknown geometry, and geometry left empty, yields no feature.
/// <para>
/// For merging a feature's pieces across tiles (XD01-463, <c>PMTilesPackFeatureMerger</c>) a piece can instead be built
/// in its zoom's tile grid (<see cref="ToTileGrid"/>: one unit per tile, a tile's column and row at its north-west
/// corner, y south), where the vertices of every tile of the zoom sit on one exact grid, and the merged shape placed on
/// the map with <see cref="FromTileGrid"/>.
/// </para>
/// </summary>
internal static class MvtFeatureProjector
{
    /// <summary>The custom attribute naming the vector tile layer a feature came from.</summary>
    public const string LayerAttribute = "mvt:layer";

    private const int CoordinateDecimals = 7;

    /// <summary>The factory of tile grid geometry: no SRID, since tile units are no map's coordinates.</summary>
    public static readonly GeometryFactory TileGrid = GeoFactory.For(0);

    /// <summary>
    /// The feature <paramref name="feature"/> of <paramref name="layer"/> in <paramref name="tile"/> as a
    /// <see cref="GeoFeature"/> with id <paramref name="id"/>, or <see langword="null"/> when it has no drawable geometry.
    /// </summary>
    public static GeoFeature? ToGeoFeature(MvtFeature feature, MvtLayer layer, TileAddress tile, string id)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(layer);

        var placement = new Placement(tile.Envelope3857, layer.Extent);
        var geometry = Build(feature, layer.Extent, GeoFactory.Wgs84, placement.Place);
        return geometry is null ? null : Create(feature, layer, geometry, id);
    }

    /// <summary>
    /// The geometry of <paramref name="feature"/> of <paramref name="layer"/> in <paramref name="tile"/>, buffer included
    /// (points in it dropped, as above), in the tile grid of the tile's zoom: x = column + tile x / extent, y = row +
    /// tile y / extent, exact for a power-of-two extent. <see langword="null"/> when it has no drawable geometry.
    /// </summary>
    public static Geometry? ToTileGrid(MvtFeature feature, MvtLayer layer, TileAddress tile)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(layer);

        double extent = layer.Extent;
        return Build(feature, layer.Extent, TileGrid, p => new Coordinate(tile.X + (p.X / extent), tile.Y + (p.Y / extent)));
    }

    /// <summary>
    /// <paramref name="grid"/>, a geometry in the tile grid of <paramref name="zoom"/>, on the map: EPSG:4326, longitude
    /// first, rounded like every projected feature. <see langword="null"/> when nothing is left.
    /// </summary>
    public static Geometry? FromTileGrid(Geometry grid, int zoom)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (grid.IsEmpty)
            return null;

        var placed = GeoFactory.Wgs84.CreateGeometry(grid);
        placed.Apply(new TileGridPlacement(TileAddress.TilesPerAxis(zoom)));
        return placed;
    }

    /// <summary>A <see cref="GeoFeature"/> with id <paramref name="id"/>, map geometry <paramref name="geometry"/> and the properties of <paramref name="feature"/> of <paramref name="layer"/>.</summary>
    public static GeoFeature Create(MvtFeature feature, MvtLayer layer, Geometry geometry, string id)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(geometry);

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

    private static Geometry? Build(MvtFeature feature, int extent, GeometryFactory factory, Func<MvtPoint, Coordinate> place)
    {
        Geometry? geometry = feature.GeometryType switch
        {
            MvtGeometryType.Point => Points(feature, extent, factory, place),
            MvtGeometryType.LineString => Lines(feature, factory, place),
            MvtGeometryType.Polygon => Polygons(feature, factory, place),
            _ => null,
        };
        return geometry is null || geometry.IsEmpty ? null : geometry;
    }

    private static Geometry? Points(MvtFeature feature, int extent, GeometryFactory factory, Func<MvtPoint, Coordinate> place)
    {
        var points = feature.Parts
            .SelectMany(part => part)
            .Where(p => p.X >= 0 && p.X < extent && p.Y >= 0 && p.Y < extent)
            .Select(p => factory.CreatePoint(place(p)))
            .ToArray();

        return points.Length switch
        {
            0 => null,
            1 => points[0],
            _ => factory.CreateMultiPoint(points),
        };
    }

    private static Geometry? Lines(MvtFeature feature, GeometryFactory factory, Func<MvtPoint, Coordinate> place)
    {
        var lines = feature.Parts
            .Where(part => part.Count >= 2)
            .Select(part => factory.CreateLineString([.. part.Select(place)]))
            .ToArray();

        return lines.Length switch
        {
            0 => null,
            1 => lines[0],
            _ => factory.CreateMultiLineString(lines),
        };
    }

    private static Geometry? Polygons(MvtFeature feature, GeometryFactory factory, Func<MvtPoint, Coordinate> place)
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
            var ring = Ring(part, factory, place);
            if (Math.Sign(area) == exteriorSign)
            {
                if (shell is not null)
                    polygons.Add(factory.CreatePolygon(shell, [.. holes]));
                shell = ring;
                holes.Clear();
            }
            else
            {
                holes.Add(ring);
            }
        }

        polygons.Add(factory.CreatePolygon(shell!, [.. holes]));
        return polygons.Count == 1 ? polygons[0] : factory.CreateMultiPolygon([.. polygons]);
    }

    private static LinearRing Ring(IReadOnlyList<MvtPoint> part, GeometryFactory factory, Func<MvtPoint, Coordinate> place)
    {
        var coordinates = new Coordinate[part.Count + 1];
        for (var i = 0; i < part.Count; i++)
            coordinates[i] = place(part[i]);
        coordinates[^1] = coordinates[0].Copy();
        return factory.CreateLinearRing(coordinates);
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

    /// <summary>Places tile grid coordinates on the map: the world's Web Mercator square cut into <c>tiles</c> units per side, unprojected and rounded.</summary>
    private sealed class TileGridPlacement(int tiles) : ICoordinateSequenceFilter
    {
        private readonly double _unit = 2 * WebMercator.HalfWorldSize / tiles;

        public bool Done => false;

        public bool GeometryChanged => true;

        public void Filter(CoordinateSequence seq, int i)
        {
            var (longitude, latitude) = WebMercator.Unproject(
                -WebMercator.HalfWorldSize + (seq.GetX(i) * _unit),
                WebMercator.HalfWorldSize - (seq.GetY(i) * _unit));
            seq.SetX(i, Math.Round(longitude, CoordinateDecimals));
            seq.SetY(i, Math.Round(latitude, CoordinateDecimals));
        }
    }
}
