using Xunit;

namespace GeoAssets.Bench.Render.Tests;

/// <summary>Runs only against the benchmark database, when <see cref="RasterTierBenchmarkTests.PolicyVariable"/> names a <c>calibrate --out</c> file.</summary>
internal sealed class RasterBenchFactAttribute : FactAttribute
{
    public RasterBenchFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RasterTierBenchmarkTests.PolicyVariable)))
            Skip = $"Measures the raster tier on the benchmark database: set {RasterTierBenchmarkTests.PolicyVariable} to a 'calibrate --out' file.";
    }
}
