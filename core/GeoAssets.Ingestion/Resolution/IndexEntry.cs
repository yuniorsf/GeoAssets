using System.Runtime.InteropServices;

namespace GeoAssets.Ingestion.Resolution;

/// <summary>Compact reference-index entry: the element's feature id and its representative native point.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct IndexEntry(Guid FeatureId, double X, double Y)
{
    public string FeatureIdText => FeatureId.ToString();

    public double DistanceTo(double x, double y)
    {
        var dx = X - x;
        var dy = Y - y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
