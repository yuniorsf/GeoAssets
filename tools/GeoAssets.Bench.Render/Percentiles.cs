using System.Runtime.InteropServices;

namespace GeoAssets.Bench.Render;

/// <summary>A sample's p50 and p95 (nearest-rank), and its count.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct PercentileSummary(double P50, double P95, int Count)
{
    public static PercentileSummary Of(IEnumerable<double> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var sorted = samples.Order().ToArray();
        if (sorted.Length == 0) throw new ArgumentException("At least one sample is needed.", nameof(samples));
        return new PercentileSummary(NearestRank(sorted, 50), NearestRank(sorted, 95), sorted.Length);
    }

    /// <summary>The smallest sample with at least <paramref name="percentile"/>% of the samples at or below it.</summary>
    internal static double NearestRank(IReadOnlyList<double> sorted, double percentile)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        var rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Count);
        return sorted[Math.Clamp(rank, 1, sorted.Count) - 1];
    }
}
