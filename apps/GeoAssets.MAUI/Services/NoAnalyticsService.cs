using GeoAssets.Shared.Services;

namespace GeoAssets.MAUI.Services;

/// <summary>
/// The app's <see cref="IAnalyticsService"/> (XD01-476): the events the shared panels emit (the data sources panel's
/// among them) go nowhere — this app exports no telemetry from the device (see <c>MauiProgram.AddCrossCuttingServices</c>).
/// </summary>
internal sealed class NoAnalyticsService : IAnalyticsService
{
    public void TrackEvent(string name, object? properties = null) { }

    public void TrackException(string message, object? properties = null) { }

    public void TrackMetric(string name, double value, object? properties = null) { }
}
