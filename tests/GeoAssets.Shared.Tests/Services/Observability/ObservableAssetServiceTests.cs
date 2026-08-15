using System.Diagnostics;
using FluentAssertions;
using GeoAssets.Core.Diagnostics;
using GeoAssets.Core.Services;
using GeoAssets.Shared.Services.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.Observability;

public class ObservableAssetServiceTests
{
    /// <summary>Advances the fake clock by <paramref name="simulatedDuration"/> while "doing the work".</summary>
    private sealed class DelayingAssetService(FakeTimeProvider timeProvider, TimeSpan simulatedDuration) : IAssetService
    {
        public string CollectionName { get; set; } = string.Empty;
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<string> ExportAsync() => Task.FromResult(string.Empty);
        public Task ClearAllAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task ImportAsync(string geoJson)
        {
            timeProvider.Advance(simulatedDuration);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ImportAsync_RecordsDurationMsTag_MatchingFakeAdvancedElapsedTime()
    {
        var timeProvider = new FakeTimeProvider();
        var simulatedDuration = TimeSpan.FromMilliseconds(250);
        var inner = new DelayingAssetService(timeProvider, simulatedDuration);
        var sut = new ObservableAssetService(inner, NullLogger<ObservableAssetService>.Instance, timeProvider);

        Activity? captured = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ImportDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => captured = activity
        };
        ActivitySource.AddActivityListener(listener);

        await sut.ImportAsync("""{"type":"FeatureCollection","features":[]}""");

        captured.Should().NotBeNull();
        captured!.GetTagItem("duration_ms").Should().Be((long)simulatedDuration.TotalMilliseconds);
    }
}
