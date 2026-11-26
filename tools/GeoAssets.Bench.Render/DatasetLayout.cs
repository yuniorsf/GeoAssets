using System.Runtime.InteropServices;

namespace GeoAssets.Bench.Render;

/// <summary>An axis-aligned lon/lat box (EPSG:4326, degrees).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct GeoBounds(double MinLon, double MinLat, double MaxLon, double MaxLat)
{
    public bool Contains(double lon, double lat) => lon >= MinLon && lon <= MaxLon && lat >= MinLat && lat <= MaxLat;
}

/// <summary>A town: where a share of the features cluster, normally distributed around its center.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Cluster(double Lon, double Lat, double SigmaDegrees, double Weight);

/// <summary>
/// Where the benchmark's features lie: a 2.5° × 2.0° utility concession (south-east Brazil, around
/// −46.25 / −23.0) holding <see cref="ClusterCount"/> towns — one large metro core — that gather
/// <see cref="ClusteredShare"/> of the features, the rest spread uniformly (rural network). Derived from the seed alone,
/// so the dataset and the viewports sampled over it are the same on every run.
/// </summary>
internal sealed class DatasetLayout
{
    public const int ClusterCount = 12;
    public const double ClusteredShare = 0.75;

    public static readonly GeoBounds Extent = new(-47.50, -24.00, -45.00, -22.00);

    private readonly double _totalWeight;

    public DatasetLayout(ulong seed)
    {
        var rng = new SplitMix64(seed ^ 0xC1A5_7E25UL);
        var clusters = new List<Cluster>(ClusterCount)
        {
            // The metro core: the densest area, where most z16 viewports land.
            new(Lon: -46.63, Lat: -23.55, SigmaDegrees: 0.12, Weight: 4.0),
        };
        for (var i = 1; i < ClusterCount; i++)
        {
            clusters.Add(new Cluster(
                rng.NextDouble(Extent.MinLon + 0.2, Extent.MaxLon - 0.2),
                rng.NextDouble(Extent.MinLat + 0.2, Extent.MaxLat - 0.2),
                rng.NextDouble(0.02, 0.08),
                rng.NextDouble(0.5, 1.5)));
        }

        Clusters = clusters;
        _totalWeight = clusters.Sum(c => c.Weight);
    }

    public IReadOnlyList<Cluster> Clusters { get; }

    /// <summary>A position drawn from the layout's density: a town (by weight) or, for the rest, anywhere in the extent.</summary>
    public (double Lon, double Lat) SamplePosition(SplitMix64 rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        if (rng.NextDouble() >= ClusteredShare)
            return (rng.NextDouble(Extent.MinLon, Extent.MaxLon), rng.NextDouble(Extent.MinLat, Extent.MaxLat));

        var pick = rng.NextDouble() * _totalWeight;
        var cluster = Clusters[^1];
        foreach (var candidate in Clusters)
        {
            if (pick < candidate.Weight)
            {
                cluster = candidate;
                break;
            }

            pick -= candidate.Weight;
        }

        var lon = cluster.Lon + (rng.NextGaussian() * cluster.SigmaDegrees);
        var lat = cluster.Lat + (rng.NextGaussian() * cluster.SigmaDegrees);
        return (Math.Clamp(lon, Extent.MinLon, Extent.MaxLon), Math.Clamp(lat, Extent.MinLat, Extent.MaxLat));
    }
}
