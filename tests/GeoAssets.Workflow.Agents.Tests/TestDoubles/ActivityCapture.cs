using System.Diagnostics;
using GeoAssets.Infrastructure.Observability;

namespace GeoAssets.Workflow.Agents.Tests.TestDoubles;

/// <summary>Captures every span stopped on <see cref="GeoAssetsActivitySource.SourceName"/> while listening.</summary>
internal sealed class ActivityCapture : IDisposable
{
    private readonly ActivityListener _listener;
    public List<Activity> Activities { get; } = [];

    public ActivityCapture()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, GeoAssetsActivitySource.SourceName, StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = Activities.Add,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();
}
