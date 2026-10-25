using System.Diagnostics;
using GeoAssets.Infrastructure.Observability;

namespace GeoAssets.Workflow.Agents.Tests.TestDoubles;

/// <summary>
/// Shared observability test double: a real <see cref="GeoAssetsActivitySource"/> with a
/// process-wide <see cref="ActivityListener"/> registered once (without one, every
/// <see cref="ActivitySource.StartActivity(string)"/> call returns <see langword="null"/> —
/// nothing would be sampling).
/// </summary>
internal static class TestObservability
{
    static TestObservability()
    {
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, GeoAssetsActivitySource.SourceName, StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        });
    }

    public static GeoAssetsActivitySource Tracer { get; } = new("1.0.0");
}
