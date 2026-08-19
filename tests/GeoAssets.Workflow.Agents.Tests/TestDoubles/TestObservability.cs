using System.Diagnostics;
using GeoAssets.Infrastructure.Observability;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GeoAssets.Workflow.Agents.Tests.TestDoubles;

/// <summary>
/// Groups every test class that asserts on captured <see cref="Activity"/> instances into one
/// serialized xunit collection. <see cref="ActivitySource"/> is process-global — with the default
/// per-class parallelization, two test classes' executors racing concurrently would both fire
/// activities into whichever <see cref="ActivityCapture"/> listeners happen to be registered at
/// that moment, corrupting each other's capture lists (and each other's counts).
/// </summary>
[CollectionDefinition("AgentObservability", DisableParallelization = true)]
public class AgentObservabilityCollection;

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
            ShouldListenTo = source => source.Name == GeoAssetsActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        });
    }

    public static GeoAssetsActivitySource Tracer { get; } = new("1.0.0");
}

/// <summary>Captures every log entry for assertions, without a mocking library.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception), exception));
}

/// <summary>Captures every span stopped on <see cref="GeoAssetsActivitySource.SourceName"/> while listening.</summary>
internal sealed class ActivityCapture : IDisposable
{
    private readonly ActivityListener _listener;
    public List<Activity> Activities { get; } = [];

    public ActivityCapture()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == GeoAssetsActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = Activities.Add,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();
}
