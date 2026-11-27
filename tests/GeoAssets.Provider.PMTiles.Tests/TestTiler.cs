using GeoAssets.Core.Rendering;
using NetTopologySuite.Geometries;

namespace GeoAssets.Provider.PMTiles.Tests;

/// <summary>
/// Cuts shapes into vector tile pieces the way a tiler does (XD01-463): a shape given in world pixel coordinates at one
/// zoom (<c>x = column × <see cref="Extent"/> + tile x</c>, y south) is clipped to every tile it touches grown by
/// <see cref="Buffer"/>, and each piece's vertices rounded to whole tile units — so the pieces of a polygon overlap in
/// the buffer and their clip points don't quite agree, as in a real pack.
/// </summary>
internal static class TestTiler
{
    public const int Extent = 4096;

    public const int Buffer = 64;

    private static readonly GeometryFactory Pixels = new();

    public static Polygon Polygon(params (int X, int Y)[] shell) =>
        Pixels.CreatePolygon([.. shell.Select(p => new Coordinate(p.X, p.Y)), new Coordinate(shell[0].X, shell[0].Y)]);

    public static LineString Line(params (int X, int Y)[] vertices) =>
        Pixels.CreateLineString([.. vertices.Select(p => new Coordinate(p.X, p.Y))]);

    public static MultiPoint Points(params (int X, int Y)[] points) =>
        Pixels.CreateMultiPoint([.. points.Select(p => Pixels.CreatePoint(new Coordinate(p.X, p.Y)))]);

    /// <summary>The pieces of <paramref name="shape"/> (world pixels at <paramref name="zoom"/>) per tile it touches, as features with <paramref name="id"/>.</summary>
    public static IEnumerable<(TileAddress Tile, TestMvt.Feature Piece)> Cut(int zoom, Geometry shape, ulong? id,
        IReadOnlyDictionary<string, object>? properties = null)
    {
        var envelope = shape.EnvelopeInternal;
        var n = 1 << zoom;
        for (var y = Math.Max(0, (int)Math.Floor((envelope.MinY - Buffer) / Extent)); y <= Math.Min(n - 1, (int)Math.Floor((envelope.MaxY + Buffer) / Extent)); y++)
        {
            for (var x = Math.Max(0, (int)Math.Floor((envelope.MinX - Buffer) / Extent)); x <= Math.Min(n - 1, (int)Math.Floor((envelope.MaxX + Buffer) / Extent)); x++)
            {
                var box = Pixels.ToGeometry(new Envelope((x * Extent) - Buffer, ((x + 1) * Extent) + Buffer, (y * Extent) - Buffer, ((y + 1) * Extent) + Buffer));
                var clipped = shape.Intersection(box);
                if (clipped.IsEmpty)
                    continue;

                var parts = Parts(clipped, x * Extent, y * Extent);
                if (parts.Length > 0)
                    yield return (new TileAddress(zoom, x, y), new TestMvt.Feature(TypeOf(shape), parts, properties, id));
            }
        }
    }

    /// <summary>Encodes each tile's pieces as one gzip layer named <paramref name="layer"/>.</summary>
    public static PMTilesTestArchive Archive(IEnumerable<(TileAddress Tile, TestMvt.Feature Piece)> pieces, byte maxZoom, string layer = "shapes")
    {
        var archive = new PMTilesTestArchive { MaxZoom = maxZoom, MetadataJson = $$"""{"vector_layers":[{"id":"{{layer}}"}]}""" };
        foreach (var tile in pieces.GroupBy(p => p.Tile))
            archive.Add(tile.Key, PMTilesTestArchive.Gzip(TestMvt.Encode(new TestMvt.Layer(layer, [.. tile.Select(p => p.Piece)]))));
        return archive;
    }

    /// <summary>A world pixel at <paramref name="zoom"/> as longitude/latitude, the way the provider unprojects it.</summary>
    public static Coordinate ToLonLat(int zoom, double x, double y)
    {
        var world = 2 * WebMercator.HalfWorldSize;
        var pixels = (double)(1 << zoom) * Extent;
        var (longitude, latitude) = WebMercator.Unproject(-WebMercator.HalfWorldSize + (x * world / pixels), WebMercator.HalfWorldSize - (y * world / pixels));
        return new Coordinate(longitude, latitude);
    }

    /// <summary><paramref name="shape"/> (world pixels at <paramref name="zoom"/>) in longitude/latitude.</summary>
    public static Geometry ToLonLat(int zoom, Geometry shape)
    {
        var copy = shape.Copy();
        copy.Apply(new LonLatFilter(zoom));
        return copy;
    }

    /// <summary>The longitude/latitude box a hair inside tiles <paramref name="minX"/>…<paramref name="maxX"/>, <paramref name="minY"/>…<paramref name="maxY"/>.</summary>
    public static BoundingBox Inside(int zoom, int minX, int minY, int maxX, int maxY)
    {
        var northWest = new TileAddress(zoom, minX, minY).Envelope4326;
        var southEast = new TileAddress(zoom, maxX, maxY).Envelope4326;
        var lon = (northWest.MaxLon - northWest.MinLon) * 0.01;
        var lat = (northWest.MaxLat - northWest.MinLat) * 0.01;
        return new BoundingBox(northWest.MinLon + lon, southEast.MinLat + lat, southEast.MaxLon - lon, northWest.MaxLat - lat);
    }

    private static int TypeOf(Geometry shape) => shape switch
    {
        Point or MultiPoint => TestMvt.Point,
        LineString or MultiLineString => TestMvt.LineString,
        _ => TestMvt.Polygon,
    };

    private static int[][][] Parts(Geometry clipped, int originX, int originY)
    {
        var parts = new List<int[][]>();
        for (var i = 0; i < clipped.NumGeometries; i++)
        {
            switch (clipped.GetGeometryN(i))
            {
                case Point point:
                    parts.Add([Local(point.Coordinate, originX, originY)]);
                    break;
                case LineString line when line is not LinearRing:
                    if (Rounded(line.Coordinates, originX, originY) is { Length: >= 2 } vertices)
                        parts.Add(vertices);
                    break;
                case Polygon polygon:
                    if (Ring(polygon.ExteriorRing.Coordinates, originX, originY, exterior: true) is { } shell)
                    {
                        parts.Add(shell);
                        foreach (var hole in polygon.InteriorRings)
                            if (Ring(hole.Coordinates, originX, originY, exterior: false) is { } ring)
                                parts.Add(ring);
                    }

                    break;
            }
        }

        return [.. parts];
    }

    private static int[] Local(Coordinate c, int originX, int originY) =>
        [(int)Math.Round(c.X - originX), (int)Math.Round(c.Y - originY)];

    private static int[][] Rounded(Coordinate[] coordinates, int originX, int originY)
    {
        var vertices = new List<int[]>();
        foreach (var c in coordinates)
        {
            var v = Local(c, originX, originY);
            if (vertices.Count == 0 || vertices[^1][0] != v[0] || vertices[^1][1] != v[1])
                vertices.Add(v);
        }

        return [.. vertices];
    }

    /// <summary>A ring without its closing vertex, wound positive (exterior) or negative (hole) by the surveyor's formula.</summary>
    private static int[][]? Ring(Coordinate[] coordinates, int originX, int originY, bool exterior)
    {
        var vertices = Rounded(coordinates, originX, originY).ToList();
        if (vertices.Count > 1 && vertices[0][0] == vertices[^1][0] && vertices[0][1] == vertices[^1][1])
            vertices.RemoveAt(vertices.Count - 1);
        if (vertices.Count < 3)
            return null;

        long area = 0;
        for (var i = 0; i < vertices.Count; i++)
        {
            var a = vertices[i];
            var b = vertices[(i + 1) % vertices.Count];
            area += ((long)a[0] * b[1]) - ((long)b[0] * a[1]);
        }

        if (area == 0)
            return null;
        if ((area > 0) != exterior)
            vertices.Reverse();
        return [.. vertices];
    }

    private sealed class LonLatFilter(int zoom) : ICoordinateSequenceFilter
    {
        public bool Done => false;

        public bool GeometryChanged => true;

        public void Filter(CoordinateSequence seq, int i)
        {
            var c = ToLonLat(zoom, seq.GetX(i), seq.GetY(i));
            seq.SetX(i, c.X);
            seq.SetY(i, c.Y);
        }
    }
}
