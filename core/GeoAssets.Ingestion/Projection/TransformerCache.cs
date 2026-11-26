using System.Diagnostics.CodeAnalysis;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Pipeline;
using GeoAssets.Ingestion.Sources;
using GeoAssets.Ingestion.Spatial;

namespace GeoAssets.Ingestion.Projection;

/// <summary>
/// One <see cref="ICoordinateTransformer"/> per distinct CRS WKT (failures cached too). A layer without CRS, or whose
/// CRS the factory rejects, gets an <see cref="ImportIssueCodes.UnsupportedCrs"/> error and is skipped.
/// <see cref="PositionAccuracy"/> is <c>helmert</c> when any acquired transformer reports it, else the first one's,
/// else <c>none</c>.
/// </summary>
internal sealed class TransformerCache(ICoordinateTransformerFactory factory)
{
    private const string Helmert = "helmert";

    private readonly Dictionary<string, ICoordinateTransformer?> _byWkt = new(StringComparer.Ordinal);
    private string? _firstAccuracy;
    private bool _anyHelmert;

    public string PositionAccuracy => _anyHelmert ? Helmert : _firstAccuracy ?? "none";

    public bool TryAcquire(LayerFileSet layer, IssueLog issues, [NotNullWhen(true)] out ICoordinateTransformer? transformer)
    {
        transformer = null;
        var wkt = layer.CrsWkt;
        if (string.IsNullOrWhiteSpace(wkt))
        {
            Reject(layer, issues, "The layer has no coordinate reference system (.prj).");
            return false;
        }

        if (!_byWkt.TryGetValue(wkt, out var cached))
        {
            cached = Create(wkt);
            _byWkt[wkt] = cached;
        }

        if (cached is null)
        {
            Reject(layer, issues, "The layer's coordinate reference system is not supported.");
            return false;
        }

        _firstAccuracy ??= cached.Accuracy;
        _anyHelmert |= string.Equals(cached.Accuracy, Helmert, StringComparison.Ordinal);
        transformer = cached;
        return true;
    }

    private ICoordinateTransformer? Create(string wkt)
    {
        try
        {
            return factory.Create(wkt);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static void Reject(LayerFileSet layer, IssueLog issues, string message) =>
        issues.Add(IssueSeverity.Error, ImportIssueCodes.UnsupportedCrs, layer.GeometryPath ?? string.Concat(layer.Region, "/", layer.Layer), message);
}
