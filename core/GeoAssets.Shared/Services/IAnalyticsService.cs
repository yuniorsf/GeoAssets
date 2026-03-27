namespace GeoAssets.Shared.Services;

/// <summary>
/// Abstraction over a telemetry sink (Application Insights, no-op, etc.).
/// Allows <c>GeoAssets.Shared</c> components to emit custom events without
/// taking a hard dependency on the JS SDK or any specific host.
/// </summary>
public interface IAnalyticsService
{
    void TrackEvent(string name, object? properties = null);
    void TrackException(string message, object? properties = null);
    void TrackMetric(string name, double value, object? properties = null);
}
