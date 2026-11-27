using System.Runtime.InteropServices;

namespace GeoAssets.Core.Rendering;

/// <summary>The nearest-rank p50, p95 and p99 of a density (features or vertices per tile) over the sampled tiles (XD01-439).</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct DensityPercentiles(long P50, long P95, long P99)
{
    /// <summary>The percentiles of <paramref name="values"/>; all zero when there is none.</summary>
    public static DensityPercentiles Of(IEnumerable<long> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var sorted = values.Order().ToArray();
        return sorted.Length == 0
            ? default
            : new DensityPercentiles(NearestRank(sorted, 50), NearestRank(sorted, 95), NearestRank(sorted, 99));
    }

    /// <summary>The smallest value with at least <paramref name="percentile"/>% of <paramref name="sorted"/> at or below it.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sorted"/> is empty, or <paramref name="percentile"/> is outside [1, 100].</exception>
    public static long NearestRank(IReadOnlyList<long> sorted, int percentile)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        ArgumentOutOfRangeException.ThrowIfZero(sorted.Count, nameof(sorted));
        ArgumentOutOfRangeException.ThrowIfLessThan(percentile, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percentile, 100);

        var rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Count);
        return sorted[Math.Clamp(rank, 1, sorted.Count) - 1];
    }
}
