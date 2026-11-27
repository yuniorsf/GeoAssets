using System.Globalization;
using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PMTiles.Mvt;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Linemerge;
using NetTopologySuite.Operation.OverlayNG;

namespace GeoAssets.Provider.PMTiles.Packs;

/// <summary>
/// Joins the pieces a vector tile pack cuts a feature into, one per tile, back into one <see cref="GeoFeature"/> per
/// feature for one read of <see cref="PMTilesPackAssetProvider"/> (XD01-463), so Leaflet draws no outline along a tile
/// seam and no feature once per tile.
/// <para>
/// <b>Identity.</b> A piece belongs to a feature by its MVT feature id (MVT 2.1 §4.2: unique among the features of its
/// layer), within its layer name and geometry type; an <c>id</c> tag is not an identity (the spec gives it no meaning)
/// and is kept as a property only. A feature with an id is answered as
/// <c>pmtiles:{pack id}:{z}:{point|line|polygon}:{mvt id}:{layer}</c>, the same whichever tiles a read covers. Pieces
/// without an id can't be told apart from other features, and an id repeated within one tile's layer (some tilers give
/// every merged feature id 0) is no identity either: those pieces stay one feature per tile, buffer included, with
/// the tile's id <c>pmtiles:{pack id}:{z}/{x}/{y}:{layer index}:{n}</c> — the behaviour before XD01-463.
/// </para>
/// <para>
/// <b>Whole pieces.</b> A feature in one piece is answered as that piece, as before. So is one whose pieces include one
/// that reaches as far as all the others (its envelope covers theirs) — a small polygon or short line across a seam
/// lies whole in the neighbouring tile's buffer. That piece is the whole feature as far as the read knows: a tile clips
/// its piece to its box (the tile plus its buffer), so whatever it clips away lies beyond one of the box's lines, and a
/// piece that has it reaches past that line too. This skips the union for most features that cross a seam.
/// </para>
/// <para>
/// <b>Where.</b> Other pieces are joined in the read's tile grid (<see cref="MvtFeatureProjector.ToTileGrid"/>), not in
/// EPSG:4326: there every vertex of every tile is a multiple of 1 / extent, so the pieces' shared vertices are equal
/// exactly and the overlay runs with that grid as its fixed precision model — snap-rounding, robust for valid pieces,
/// with every output vertex on the grid. Projected first, the pieces' coordinates would carry each tile's rounding and
/// Web Mercator's non-linear latitude, and the union would leave slivers along the seams. The joined shape is placed on
/// the map once, afterwards.
/// </para>
/// <para>
/// <b>How.</b> Polygons: the union of the pieces (<see cref="UnaryUnionNG"/>). A tile's piece reaches the tile's buffer,
/// so neighbouring pieces overlap by twice the buffer and each piece's clip edges lie inside its neighbour: the union
/// dissolves them, and the outline left is the feature's own, within a grid unit where the two tiles' roundings of the
/// clip points differ. A polygon whose pieces the overlay still refuses (invalid pieces) is answered as its pieces, one
/// feature still. Lines: overlapping buffers can't be merged as they are (each tile's clip point is its own rounding),
/// so each piece is cut to its own tile without the buffer, the ends the cut makes on a shared edge are welded when
/// within <see cref="WeldTolerance"/> tile units (of the layer's extent) of each other, twice a tile's worst rounding,
/// and the lines merged (<see cref="LineMerger"/>) end to end. Points: the distinct points of the pieces (a point in a
/// tile's buffer is already left to its own tile).
/// </para>
/// </summary>
internal sealed class PMTilesPackFeatureMerger(string packId)
{
    /// <summary>How close, in units of the layer's extent, two cut line ends must be to be welded.</summary>
    public const double WeldTolerance = 2;

    private readonly List<Entry> _entries = [];
    private readonly Dictionary<FeatureKey, Group> _groups = [];

    /// <summary>Adds the features of <paramref name="layers"/>, decoded from <paramref name="tile"/>; answers how many pieces with drawable geometry it took.</summary>
    public int Add(TileAddress tile, IReadOnlyList<MvtLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);

        var added = 0;
        var inTile = new HashSet<FeatureKey>();
        for (var l = 0; l < layers.Count; l++)
        {
            var layer = layers[l];
            for (var n = 0; n < layer.Features.Count; n++)
            {
                var feature = layer.Features[n];
                if (feature is { Id: { } id, GeometryType: MvtGeometryType.Point or MvtGeometryType.LineString or MvtGeometryType.Polygon })
                {
                    if (MvtFeatureProjector.ToTileGrid(feature, layer, tile) is not { } grid)
                        continue;

                    var key = new FeatureKey(tile.Z, layer.Name, feature.GeometryType, id);
                    if (!_groups.TryGetValue(key, out var group))
                    {
                        group = new Group(key);
                        _groups.Add(key, group);
                        _entries.Add(new Entry(null, group));
                    }

                    if (!inTile.Add(key))
                        group.Ambiguous = true;
                    group.Pieces.Add(new Piece(tile, l, n, feature, layer, grid));
                    added++;
                }
                else if (MvtFeatureProjector.ToGeoFeature(feature, layer, tile, TileFeatureId(tile, l, n)) is { } single)
                {
                    _entries.Add(new Entry(single, null));
                    added++;
                }
            }
        }

        return added;
    }

    /// <summary>The features added so far, each feature with an id joined from its pieces, in the order first seen.</summary>
    public List<GeoFeature> Build(CancellationToken ct)
    {
        var features = new List<GeoFeature>(_entries.Count);
        foreach (var entry in _entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.Feature is { } single)
            {
                features.Add(single);
                continue;
            }

            var group = entry.Group!;
            if (group.Ambiguous)
            {
                foreach (var piece in group.Pieces)
                {
                    if (MvtFeatureProjector.ToGeoFeature(piece.Feature, piece.Layer, piece.Tile, TileFeatureId(piece.Tile, piece.LayerIndex, piece.Index)) is { } perTile)
                        features.Add(perTile);
                }

                continue;
            }

            if (Whole(group.Pieces) is { } whole)
            {
                if (MvtFeatureProjector.ToGeoFeature(whole.Feature, whole.Layer, whole.Tile, FeatureId(group.Key)) is { } feature)
                    features.Add(feature);
                continue;
            }

            var joined = group.Key.Type switch
            {
                MvtGeometryType.Point => JoinPoints(group),
                MvtGeometryType.LineString => JoinLines(group),
                _ => JoinPolygons(group),
            };
            if (joined is not null && MvtFeatureProjector.FromTileGrid(joined, group.Key.Zoom) is { } placed)
            {
                var first = group.Pieces[0];
                features.Add(MvtFeatureProjector.Create(first.Feature, first.Layer, placed, FeatureId(group.Key)));
            }
        }

        return features;
    }

    private string TileFeatureId(TileAddress tile, int layerIndex, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"pmtiles:{packId}:{tile}:{layerIndex}:{index}");

    private string FeatureId(FeatureKey key)
    {
        var kind = key.Type switch
        {
            MvtGeometryType.Point => "point",
            MvtGeometryType.LineString => "line",
            _ => "polygon",
        };
        return string.Create(CultureInfo.InvariantCulture, $"pmtiles:{packId}:{key.Zoom}:{kind}:{key.Id}:{key.Layer}");
    }

    /// <summary>The piece whose envelope covers every other piece's, if any: the whole feature, as the type describes.</summary>
    private static Piece? Whole(List<Piece> pieces)
    {
        foreach (var candidate in pieces)
        {
            var envelope = candidate.Grid.EnvelopeInternal;
            if (pieces.TrueForAll(p => envelope.Covers(p.Grid.EnvelopeInternal)))
                return candidate;
        }

        return null;
    }

    private static Geometry? JoinPoints(Group group)
    {
        var factory = MvtFeatureProjector.TileGrid;
        var points = group.Pieces
            .SelectMany(p => p.Grid.Coordinates)
            .Distinct()
            .Select(factory.CreatePoint)
            .ToArray();
        return points.Length == 1 ? points[0] : factory.CreateMultiPoint(points);
    }

    private static Geometry? JoinPolygons(Group group)
    {
        var factory = MvtFeatureProjector.TileGrid;
        var pieces = factory.BuildGeometry(group.Pieces.Select(p => p.Grid));
        try
        {
            var union = UnaryUnionNG.Union(pieces, new PrecisionModel(group.Pieces.Max(p => p.Layer.Extent)));
            return union.IsEmpty ? null : union;
        }
        catch (TopologyException)
        {
            return factory.CreateMultiPolygon([.. group.Pieces.SelectMany(p => Polygons(p.Grid))]);
        }
    }

    private static Geometry? JoinLines(Group group)
    {
        var runs = new List<List<Coordinate>>();
        foreach (var piece in group.Pieces)
        {
            var tile = new Envelope(piece.Tile.X, piece.Tile.X + 1, piece.Tile.Y, piece.Tile.Y + 1);
            for (var i = 0; i < piece.Grid.NumGeometries; i++)
                runs.AddRange(Cut(piece.Grid.GetGeometryN(i).Coordinates, tile));
        }

        if (runs.Count == 0)
            return null;

        Weld(runs, WeldTolerance / group.Pieces.Max(p => p.Layer.Extent));
        var factory = MvtFeatureProjector.TileGrid;
        var merger = new LineMerger();
        foreach (var run in runs)
            merger.Add(factory.CreateLineString([.. run]));

        var lines = merger.GetMergedLineStrings().Cast<LineString>().ToArray();
        return lines.Length == 1 ? lines[0] : factory.CreateMultiLineString(lines);
    }

    private static IEnumerable<Polygon> Polygons(Geometry geometry)
    {
        for (var i = 0; i < geometry.NumGeometries; i++)
        {
            if (geometry.GetGeometryN(i) is Polygon polygon)
                yield return polygon;
        }
    }

    /// <summary>The parts of the line through <paramref name="vertices"/> inside <paramref name="box"/> (Liang–Barsky, per segment), each of at least two distinct vertices.</summary>
    internal static List<List<Coordinate>> Cut(Coordinate[] vertices, Envelope box)
    {
        var runs = new List<List<Coordinate>>();
        List<Coordinate>? run = null;
        for (var i = 0; i + 1 < vertices.Length; i++)
        {
            if (!Clip(vertices[i], vertices[i + 1], box, out var a, out var b))
            {
                run = null;
                continue;
            }

            if (run is null || !run[^1].Equals2D(a))
            {
                run = [a];
                runs.Add(run);
            }

            if (!run[^1].Equals2D(b))
                run.Add(b);
        }

        runs.RemoveAll(r => r.Count < 2);
        return runs;
    }

    private static bool Clip(Coordinate p, Coordinate q, Envelope box, out Coordinate a, out Coordinate b)
    {
        double dx = q.X - p.X, dy = q.Y - p.Y, t0 = 0, t1 = 1;
        if (!Narrow(-dx, p.X - box.MinX, ref t0, ref t1)
            || !Narrow(dx, box.MaxX - p.X, ref t0, ref t1)
            || !Narrow(-dy, p.Y - box.MinY, ref t0, ref t1)
            || !Narrow(dy, box.MaxY - p.Y, ref t0, ref t1))
        {
            a = b = p;
            return false;
        }

        a = t0 == 0 ? p.Copy() : OnBox(p.X + (t0 * dx), p.Y + (t0 * dy), box);
        b = t1 == 1 ? q.Copy() : OnBox(p.X + (t1 * dx), p.Y + (t1 * dy), box);
        return true;
    }

    private static bool Narrow(double denominator, double numerator, ref double t0, ref double t1)
    {
        if (denominator == 0)
            return numerator >= 0;

        var t = numerator / denominator;
        if (denominator < 0)
        {
            if (t > t1)
                return false;
            if (t > t0)
                t0 = t;
        }
        else
        {
            if (t < t0)
                return false;
            if (t < t1)
                t1 = t;
        }

        return true;
    }

    private static Coordinate OnBox(double x, double y, Envelope box) =>
        new(Math.Clamp(x, box.MinX, box.MaxX), Math.Clamp(y, box.MinY, box.MaxY));

    /// <summary>Moves every run end within <paramref name="tolerance"/> of an earlier end of another run onto it, so the line merger sees them meet.</summary>
    private static void Weld(List<List<Coordinate>> runs, double tolerance)
    {
        var ends = new List<Coordinate>(runs.Count * 2);
        foreach (var run in runs)
        {
            ends.Add(run[0]);
            ends.Add(run[^1]);
        }

        for (var j = 2; j < ends.Count; j++)
        {
            for (var i = 0; i < j - (j % 2); i++)
            {
                if (ends[i].Distance(ends[j]) > tolerance)
                    continue;

                ends[j].X = ends[i].X;
                ends[j].Y = ends[i].Y;
                break;
            }
        }
    }

    private readonly record struct FeatureKey(int Zoom, string Layer, MvtGeometryType Type, ulong Id);

    private sealed record Piece(TileAddress Tile, int LayerIndex, int Index, MvtFeature Feature, MvtLayer Layer, Geometry Grid);

    private sealed record Entry(GeoFeature? Feature, Group? Group);

    private sealed class Group(FeatureKey key)
    {
        public FeatureKey Key { get; } = key;

        public List<Piece> Pieces { get; } = [];

        public bool Ambiguous { get; set; }
    }
}
