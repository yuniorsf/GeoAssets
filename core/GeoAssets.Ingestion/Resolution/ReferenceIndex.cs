using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace GeoAssets.Ingestion.Resolution;

/// <summary>
/// The pass-1 reference index: elements by (namespace, key) — first one indexed wins — and an STRtree of
/// snap-target points, both in the source's native (projected, metre) CRS.
/// </summary>
internal sealed class ReferenceIndex
{
    private readonly Dictionary<IndexKey, IndexEntry> _byKey = [];
    private readonly STRtree<IndexEntry> _snapTargets = new();

    public void AddKey(IndexKey key, IndexEntry entry) => _byKey.TryAdd(key, entry);

    public void AddSnapTarget(IndexEntry entry) =>
        _snapTargets.Insert(new Envelope(entry.X, entry.X, entry.Y, entry.Y), entry);

    public bool TryGet(IndexKey key, out IndexEntry entry) => _byKey.TryGetValue(key, out entry);

    /// <summary>The nearest snap target within <paramref name="tolerance"/> (ties: lowest feature id).</summary>
    public bool TryFindNearest(double x, double y, double tolerance, out IndexEntry nearest, out double distance)
    {
        nearest = default;
        distance = double.PositiveInfinity;
        var found = false;
        foreach (var candidate in _snapTargets.Query(new Envelope(x - tolerance, x + tolerance, y - tolerance, y + tolerance)))
        {
            var d = candidate.DistanceTo(x, y);
            if (d > tolerance)
            {
                continue;
            }

            if (!found || d < distance || (d.Equals(distance) && candidate.FeatureId.CompareTo(nearest.FeatureId) < 0))
            {
                nearest = candidate;
                distance = d;
                found = true;
            }
        }

        return found;
    }
}
