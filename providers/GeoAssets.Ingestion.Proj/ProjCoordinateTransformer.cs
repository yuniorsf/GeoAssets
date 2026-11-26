using GeoAssets.Ingestion.Spatial;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using OSGeo.OSR;

namespace GeoAssets.Ingestion.Proj;

/// <summary>
/// Transforms geometries in two steps: GDAL/OSR from the source CRS to <paramref name="target"/> (traditional GIS
/// axis order, x = lon, y = lat), then — for SAD69 — the datum shift <paramref name="shiftPipeline"/> over lon/lat
/// degrees. When the shift fails for a geometry (e.g. outside the grid), <paramref name="fallbackPipeline"/> is used.
/// The output is a copy stamped SRID 4326. Not thread-safe by itself: calls are serialized.
/// </summary>
internal sealed class ProjCoordinateTransformer : ICoordinateTransformer, IDisposable
{
    private static readonly GeometryFactory OutputFactory = NtsGeometryServices.Instance.CreateGeometryFactory(4326);

    private readonly Lock _sync = new();
    private readonly CoordinateTransformation _toTarget;
    private readonly CoordinateTransformation? _shift;
    private readonly CoordinateTransformation? _fallback;

    public ProjCoordinateTransformer(SpatialReference source, SpatialReference target, string accuracy, string? shiftPipeline, string? fallbackPipeline)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        Accuracy = accuracy;
        _toTarget = new CoordinateTransformation(source, target);
        _shift = shiftPipeline is null ? null : CreateOperation(shiftPipeline);
        _fallback = fallbackPipeline is null ? null : CreateOperation(fallbackPipeline);
    }

    public string Accuracy { get; }

    public Geometry Transform(Geometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var copy = geometry.Copy();
        lock (_sync)
            copy.Apply(new TransformFilter(this));
        return OutputFactory.CreateGeometry(copy);
    }

    public void Dispose()
    {
        _toTarget.Dispose();
        _shift?.Dispose();
        _fallback?.Dispose();
    }

    private static CoordinateTransformation CreateOperation(string pipeline)
    {
        using var options = new CoordinateTransformationOptions();
        options.SetOperation(pipeline, false);
        return new CoordinateTransformation(null, null, options);
    }

    private void TransformInPlace(double[] xs, double[] ys)
    {
        if (!TryApply(_toTarget, xs, ys))
            throw new InvalidOperationException("PROJ could not transform a coordinate to the target CRS.");
        if (_shift is null)
            return;

        var geographicX = (double[])xs.Clone();
        var geographicY = (double[])ys.Clone();
        if (TryApply(_shift, xs, ys))
            return;

        Array.Copy(geographicX, xs, xs.Length);
        Array.Copy(geographicY, ys, ys.Length);
        if (_fallback is null || !TryApply(_fallback, xs, ys))
            throw new InvalidOperationException("PROJ could not apply the datum shift to a coordinate.");
    }

    private static bool TryApply(CoordinateTransformation transformation, double[] xs, double[] ys)
    {
        try
        {
            transformation.TransformPoints(xs.Length, xs, ys, new double[xs.Length]);
        }
        catch (ApplicationException)
        {
            return false;
        }

        for (var i = 0; i < xs.Length; i++)
        {
            if (!double.IsFinite(xs[i]) || !double.IsFinite(ys[i]))
                return false;
        }

        return true;
    }

    private sealed class TransformFilter(ProjCoordinateTransformer owner) : IEntireCoordinateSequenceFilter
    {
        public bool Done => false;

        public bool GeometryChanged => true;

        public void Filter(CoordinateSequence seq)
        {
            var xs = new double[seq.Count];
            var ys = new double[seq.Count];
            for (var i = 0; i < seq.Count; i++)
            {
                xs[i] = seq.GetX(i);
                ys[i] = seq.GetY(i);
            }

            owner.TransformInPlace(xs, ys);
            for (var i = 0; i < seq.Count; i++)
            {
                seq.SetX(i, xs[i]);
                seq.SetY(i, ys[i]);
            }
        }
    }
}
