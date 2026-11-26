using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GeoAssets.Bench.Render;

/// <summary>What <see cref="SimulatedClientRenderer.Render"/> did with one payload.</summary>
/// <param name="ParsedFeatures">Features in the payload (each becomes a Leaflet layer).</param>
/// <param name="DrawnFeatures">Features whose asset type the map draws at the viewport's zoom.</param>
/// <param name="ProjectedVertices">Vertices projected to layer pixels (drawn features only).</param>
/// <param name="PathVertices">Vertices left in the SVG paths after Leaflet's simplification.</param>
/// <param name="PathCharacters">Total length of the SVG path data built.</param>
internal sealed record ClientRenderResult(
    TimeSpan Elapsed,
    int ParsedFeatures,
    int DrawnFeatures,
    long ProjectedVertices,
    long PathVertices,
    long PathCharacters);

/// <summary>
/// A headless stand-in for the Web client's time-to-paint of one <c>features/bounds</c> payload (XD01-420): the CPU work
/// <c>geoassets.js</c>'s <c>renderFeatureBatch</c> (SVG renderer) does before the browser paints — parse the whole body
/// (<c>JSON.parse</c>), then, for each feature whose type is drawn at this zoom (the client filters by zoom, the server does
/// not), project every vertex to layer pixels (<c>latLngToLayerPoint</c>), simplify lines/rings with Leaflet's
/// <c>LineUtil.simplify</c> (radial distance, then Douglas–Peucker, <c>smoothFactor</c> 1 px) and build the SVG path data
/// (circle markers for points). It is a lower bound measured in .NET on the bench machine: no DOM/SVG node creation,
/// style resolution, layout or rasterization, no JS engine or WASM interop, no requestAnimationFrame chunking, and no
/// viewport clipping.
/// </summary>
internal sealed class SimulatedClientRenderer(TimeProvider timeProvider)
{
    /// <summary><c>geoassets.js</c>'s default point style radius (<c>circleMarker</c>).</summary>
    private const int PointRadiusPx = 8;

    private const double SimplifyTolerancePx = 1.0;

    public ClientRenderResult Render(byte[] payload, MapViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(viewport);

        var started = timeProvider.GetTimestamp();
        var parsed = 0;
        var drawn = 0;
        long projected = 0;
        long pathVertices = 0;
        long pathCharacters = 0;
        var path = new StringBuilder(4096);
        var points = new List<(double X, double Y)>(256);

        using var document = JsonDocument.Parse(payload);
        foreach (var feature in document.RootElement.EnumerateArray())
        {
            parsed++;
            var assetTypeId = feature.GetProperty("properties").GetProperty("assetTypeId").GetString() ?? string.Empty;
            if (DatasetComposition.Find(assetTypeId) is not { } type || !type.IsDrawnAt(viewport.Zoom)) continue;
            if (!feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Object) continue;

            drawn++;
            path.Clear();
            var coordinates = geometry.GetProperty("coordinates");
            switch (geometry.GetProperty("type").GetString())
            {
                case "Point":
                    var (x, y) = viewport.ToLayerPoint(coordinates[0].GetDouble(), coordinates[1].GetDouble());
                    AppendCircle(path, x, y);
                    projected++;
                    pathVertices++;
                    break;
                case "LineString":
                    projected += AppendRing(path, coordinates, viewport, points, closed: false, ref pathVertices);
                    break;
                case "Polygon":
                    foreach (var ring in coordinates.EnumerateArray())
                        projected += AppendRing(path, ring, viewport, points, closed: true, ref pathVertices);
                    break;
                default:
                    break;
            }

            pathCharacters += path.Length;
        }

        return new ClientRenderResult(timeProvider.GetElapsedTime(started), parsed, drawn, projected, pathVertices, pathCharacters);
    }

    private static int AppendRing(
        StringBuilder path, JsonElement ring, MapViewport viewport, List<(double X, double Y)> points, bool closed, ref long pathVertices)
    {
        points.Clear();
        foreach (var position in ring.EnumerateArray())
            points.Add(viewport.ToLayerPoint(position[0].GetDouble(), position[1].GetDouble()));

        var projected = points.Count;
        var simplified = Simplify(points, SimplifyTolerancePx);
        pathVertices += simplified.Count;

        for (var i = 0; i < simplified.Count; i++)
        {
            path.Append(i == 0 ? 'M' : 'L');
            path.Append(simplified[i].X.ToString(CultureInfo.InvariantCulture));
            path.Append(' ');
            path.Append(simplified[i].Y.ToString(CultureInfo.InvariantCulture));
        }

        if (closed) path.Append('z');
        return projected;
    }

    private static void AppendCircle(StringBuilder path, double x, double y)
    {
        // Leaflet's SVG renderer: two arcs, "M x-r,y a r,r 0 1,0 2r,0 a r,r 0 1,0 -2r,0".
        var inv = CultureInfo.InvariantCulture;
        path.Append('M').Append((x - PointRadiusPx).ToString(inv)).Append(',').Append(y.ToString(inv))
            .Append(inv, $"a{PointRadiusPx},{PointRadiusPx} 0 1,0 {2 * PointRadiusPx},0 ")
            .Append(inv, $"a{PointRadiusPx},{PointRadiusPx} 0 1,0 {-2 * PointRadiusPx},0");
    }

    /// <summary>Leaflet's <c>L.LineUtil.simplify</c>: radial-distance reduction, then Douglas–Peucker.</summary>
    internal static List<(double X, double Y)> Simplify(IReadOnlyList<(double X, double Y)> points, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (tolerance <= 0 || points.Count <= 2) return [.. points];

        var sqTolerance = tolerance * tolerance;
        var reduced = ReduceRadially(points, sqTolerance);
        return DouglasPeucker(reduced, sqTolerance);
    }

    private static List<(double X, double Y)> ReduceRadially(IReadOnlyList<(double X, double Y)> points, double sqTolerance)
    {
        var reduced = new List<(double X, double Y)>(points.Count) { points[0] };
        var previous = 0;
        for (var i = 1; i < points.Count; i++)
        {
            if (SquaredDistance(points[i], points[previous]) > sqTolerance)
            {
                reduced.Add(points[i]);
                previous = i;
            }
        }

        if (previous < points.Count - 1) reduced.Add(points[^1]);
        return reduced;
    }

    private static List<(double X, double Y)> DouglasPeucker(List<(double X, double Y)> points, double sqTolerance)
    {
        var keep = new bool[points.Count];
        keep[0] = keep[^1] = true;
        MarkDouglasPeucker(points, keep, sqTolerance, 0, points.Count - 1);

        var result = new List<(double X, double Y)>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            if (keep[i]) result.Add(points[i]);
        }

        return result;
    }

    private static void MarkDouglasPeucker(List<(double X, double Y)> points, bool[] keep, double sqTolerance, int first, int last)
    {
        var maxSqDistance = 0.0;
        var index = 0;
        for (var i = first + 1; i <= last - 1; i++)
        {
            var sqDistance = SquaredSegmentDistance(points[i], points[first], points[last]);
            if (sqDistance > maxSqDistance)
            {
                index = i;
                maxSqDistance = sqDistance;
            }
        }

        if (maxSqDistance > sqTolerance)
        {
            keep[index] = true;
            MarkDouglasPeucker(points, keep, sqTolerance, first, index);
            MarkDouglasPeucker(points, keep, sqTolerance, index, last);
        }
    }

    private static double SquaredDistance((double X, double Y) a, (double X, double Y) b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy);
    }

    private static double SquaredSegmentDistance((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
        var x = a.X;
        var y = a.Y;
        var dx = b.X - x;
        var dy = b.Y - y;
        var dot = (dx * dx) + (dy * dy);

        if (dot > 0)
        {
            var t = (((p.X - x) * dx) + ((p.Y - y) * dy)) / dot;
            if (t > 1)
            {
                x = b.X;
                y = b.Y;
            }
            else if (t > 0)
            {
                x += dx * t;
                y += dy * t;
            }
        }

        dx = p.X - x;
        dy = p.Y - y;
        return (dx * dx) + (dy * dy);
    }
}
